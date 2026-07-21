#include "wasapi_loopback_capture.h"

#include <audioclient.h>
#include <endpointvolume.h>
#include <mmdeviceapi.h>
#include <windows.h>
#include <winrt/base.h>

#include <algorithm>
#include <chrono>
#include <cstring>
#include <deque>
#include <limits>
#include <stdexcept>
#include <string>
#include <utility>

namespace
{
    class local_playback_mute_error final : public std::runtime_error
    {
    public:
        using std::runtime_error::runtime_error;
    };

    constexpr GUID local_mute_event_context{
        0x7655ee7c,
        0x9250,
        0x4589,
        { 0x87, 0xf0, 0x75, 0xbc, 0xeb, 0xec, 0x82, 0x9d },
    };
    constexpr DWORD stream_flags =
        AUDCLNT_STREAMFLAGS_LOOPBACK |
        AUDCLNT_STREAMFLAGS_EVENTCALLBACK |
        AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM |
        AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
    constexpr std::size_t maximum_pending_frames =
        static_cast<std::size_t>(ddc::wasapi_loopback_capture::sample_rate) * 2U;

    struct endpoint_volume_callback :
        winrt::implements<endpoint_volume_callback, IAudioEndpointVolumeCallback>
    {
        explicit endpoint_volume_callback(const bool initial_mute) : last_mute_(initial_mute)
        {
        }

        HRESULT STDMETHODCALLTYPE OnNotify(
            PAUDIO_VOLUME_NOTIFICATION_DATA notification) noexcept final
        {
            if (notification == nullptr)
            {
                return E_INVALIDARG;
            }

            const bool new_mute = notification->bMuted != FALSE;
            const bool previous_mute = last_mute_.exchange(new_mute);
            if (!IsEqualGUID(notification->guidEventContext, local_mute_event_context) &&
                previous_mute != new_mute)
            {
                external_mute_change_.store(true);
            }

            return S_OK;
        }

        [[nodiscard]] bool external_mute_change() const noexcept
        {
            return external_mute_change_.load();
        }

    private:
        std::atomic<bool> last_mute_{};
        std::atomic<bool> external_mute_change_{};
    };

    struct capture_device final
    {
        winrt::com_ptr<IAudioClient> audio_client;
        winrt::com_ptr<IAudioCaptureClient> capture_client;
        winrt::com_ptr<IAudioEndpointVolume> endpoint_volume;
        winrt::com_ptr<endpoint_volume_callback> volume_callback;
        winrt::handle ready_event;
        std::wstring id;
        ddc::audio_event_callback mute_change_callback;
        ddc::audio_event_callback mute_restore_failure_callback;
        bool restore_mute{};

        void reset() noexcept
        {
            if (audio_client)
            {
                audio_client->Stop();
            }

            if (endpoint_volume && volume_callback)
            {
                endpoint_volume->UnregisterControlChangeNotify(volume_callback.get());
            }

            if (endpoint_volume && restore_mute &&
                (!volume_callback || !volume_callback->external_mute_change()))
            {
                BOOL currently_muted{};
                const HRESULT read_result = endpoint_volume->GetMute(&currently_muted);
                const HRESULT restore_result = SUCCEEDED(read_result) && currently_muted != FALSE
                    ? endpoint_volume->SetMute(FALSE, &local_mute_event_context)
                    : read_result;
                if (FAILED(restore_result))
                {
                    try
                    {
                        mute_restore_failure_callback();
                    }
                    catch (...)
                    {
                    }
                }
                else if (currently_muted != FALSE)
                {
                    try
                    {
                        mute_change_callback();
                    }
                    catch (...)
                    {
                    }
                }
            }

            capture_client = nullptr;
            audio_client = nullptr;
            volume_callback = nullptr;
            endpoint_volume = nullptr;
            ready_event.close();
            id.clear();
            mute_change_callback = nullptr;
            mute_restore_failure_callback = nullptr;
            restore_mute = false;
        }

        ~capture_device()
        {
            reset();
        }
    };

    [[nodiscard]] std::wstring get_device_id(IMMDevice& device)
    {
        LPWSTR value{};
        winrt::check_hresult(device.GetId(&value));
        std::wstring result;
        try
        {
            result = value == nullptr ? L"" : value;
        }
        catch (...)
        {
            CoTaskMemFree(value);
            throw;
        }

        CoTaskMemFree(value);
        return result;
    }

    [[nodiscard]] winrt::com_ptr<IMMDevice> get_default_render_device(
        IMMDeviceEnumerator& enumerator)
    {
        winrt::com_ptr<IMMDevice> device;
        winrt::check_hresult(enumerator.GetDefaultAudioEndpoint(
            eRender,
            eConsole,
            device.put()));
        return device;
    }

    void open_capture_device(
        IMMDeviceEnumerator& enumerator,
        capture_device& target,
        const bool mute_local_playback,
        ddc::audio_event_callback mute_change_callback,
        ddc::audio_event_callback mute_restore_failure_callback)
    {
        winrt::com_ptr<IMMDevice> device = get_default_render_device(enumerator);
        winrt::com_ptr<IAudioClient> audio_client;
        winrt::check_hresult(device->Activate(
            __uuidof(IAudioClient),
            CLSCTX_ALL,
            nullptr,
            audio_client.put_void()));

        WAVEFORMATEX format{};
        format.wFormatTag = WAVE_FORMAT_PCM;
        format.nChannels = ddc::wasapi_loopback_capture::channels;
        format.nSamplesPerSec = ddc::wasapi_loopback_capture::sample_rate;
        format.wBitsPerSample = 16;
        format.nBlockAlign = format.nChannels * format.wBitsPerSample / 8;
        format.nAvgBytesPerSec = format.nSamplesPerSec * format.nBlockAlign;
        winrt::check_hresult(audio_client->Initialize(
            AUDCLNT_SHAREMODE_SHARED,
            stream_flags,
            0,
            0,
            &format,
            nullptr));

        winrt::handle ready_event{ CreateEventW(nullptr, FALSE, FALSE, nullptr) };
        if (!ready_event)
        {
            winrt::throw_last_error();
        }

        winrt::check_hresult(audio_client->SetEventHandle(ready_event.get()));
        winrt::com_ptr<IAudioCaptureClient> capture_client;
        winrt::check_hresult(audio_client->GetService(
            __uuidof(IAudioCaptureClient),
            capture_client.put_void()));
        winrt::check_hresult(audio_client->Start());

        target.reset();
        target.id = get_device_id(*device);
        target.audio_client = std::move(audio_client);
        target.capture_client = std::move(capture_client);
        target.ready_event = std::move(ready_event);
        target.mute_change_callback = std::move(mute_change_callback);
        target.mute_restore_failure_callback = std::move(mute_restore_failure_callback);

        if (mute_local_playback)
        {
            try
            {
                winrt::check_hresult(device->Activate(
                    __uuidof(IAudioEndpointVolume),
                    CLSCTX_ALL,
                    nullptr,
                    target.endpoint_volume.put_void()));
                BOOL initially_muted{};
                winrt::check_hresult(target.endpoint_volume->GetMute(&initially_muted));
                target.volume_callback = winrt::make_self<endpoint_volume_callback>(
                    initially_muted != FALSE);
                winrt::check_hresult(target.endpoint_volume->RegisterControlChangeNotify(
                    target.volume_callback.get()));
                if (initially_muted == FALSE)
                {
                    winrt::check_hresult(target.endpoint_volume->SetMute(
                        TRUE,
                        &local_mute_event_context));
                    target.restore_mute = true;
                    target.mute_change_callback();
                }
            }
            catch (...)
            {
                target.reset();
                std::throw_with_nested(local_playback_mute_error(
                    "The selected Windows audio output could not be muted safely."));
            }
        }
    }

    [[nodiscard]] std::int64_t frames_from_100ns(const std::int64_t value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value * ddc::wasapi_loopback_capture::sample_rate / 10'000'000LL;
    }
}

namespace ddc
{
    wasapi_loopback_capture::~wasapi_loopback_capture()
    {
        stop();
    }

    void wasapi_loopback_capture::start(
        const std::int64_t session_start_100ns,
        const bool mute_local_playback,
        audio_pcm_callback pcm_callback,
        audio_event_callback packet_callback,
        audio_event_callback device_change_callback,
        audio_event_callback timestamp_correction_callback,
        audio_event_callback local_mute_change_callback,
        audio_event_callback local_mute_restore_failure_callback,
        audio_failure_callback failure_callback)
    {
        if (worker_.joinable() || session_start_100ns <= 0 || !pcm_callback ||
            !packet_callback || !device_change_callback || !timestamp_correction_callback ||
            !local_mute_change_callback || !local_mute_restore_failure_callback || !failure_callback)
        {
            throw std::invalid_argument("The WASAPI loopback capture configuration is invalid.");
        }

        session_start_100ns_ = session_start_100ns;
        mute_local_playback_ = mute_local_playback;
        pcm_callback_ = std::move(pcm_callback);
        packet_callback_ = std::move(packet_callback);
        device_change_callback_ = std::move(device_change_callback);
        timestamp_correction_callback_ = std::move(timestamp_correction_callback);
        local_mute_change_callback_ = std::move(local_mute_change_callback);
        local_mute_restore_failure_callback_ = std::move(local_mute_restore_failure_callback);
        failure_callback_ = std::move(failure_callback);
        stopping_.store(false);
        worker_ = std::thread([this] { run(); });
    }

    void wasapi_loopback_capture::stop() noexcept
    {
        stopping_.store(true);
        if (worker_.joinable())
        {
            worker_.join();
        }
    }

    std::int64_t wasapi_loopback_capture::monotonic_time_100ns()
    {
        LARGE_INTEGER counter{};
        LARGE_INTEGER frequency{};
        if (!QueryPerformanceCounter(&counter) || !QueryPerformanceFrequency(&frequency) ||
            frequency.QuadPart <= 0)
        {
            winrt::throw_last_error();
        }

        const long double value =
            static_cast<long double>(counter.QuadPart) * 10'000'000.0L /
            static_cast<long double>(frequency.QuadPart);
        if (value > static_cast<long double>(std::numeric_limits<std::int64_t>::max()))
        {
            throw std::overflow_error("The performance counter value is outside the media clock range.");
        }

        return static_cast<std::int64_t>(value);
    }

    void wasapi_loopback_capture::run() noexcept
    {
        bool apartment_initialized{};
        try
        {
            winrt::init_apartment(winrt::apartment_type::multi_threaded);
            apartment_initialized = true;
            winrt::com_ptr<IMMDeviceEnumerator> enumerator;
            winrt::check_hresult(CoCreateInstance(
                __uuidof(MMDeviceEnumerator),
                nullptr,
                CLSCTX_ALL,
                __uuidof(IMMDeviceEnumerator),
                enumerator.put_void()));

            capture_device device;
            std::deque<std::int16_t> pending_samples;
            std::int64_t emitted_frames{};
            std::int64_t retry_at_100ns{};
            std::int64_t device_check_at_100ns{};
            bool opened_once{};
            while (!stopping_.load())
            {
                const std::int64_t now = monotonic_time_100ns();
                if (!device.audio_client && now >= retry_at_100ns)
                {
                    try
                    {
                        open_capture_device(
                            *enumerator,
                            device,
                            mute_local_playback_,
                            local_mute_change_callback_,
                            local_mute_restore_failure_callback_);
                        if (opened_once)
                        {
                            device_change_callback_();
                        }

                        opened_once = true;
                        retry_at_100ns = 0;
                        device_check_at_100ns = now + 5'000'000LL;
                    }
                    catch (const local_playback_mute_error&)
                    {
                        throw;
                    }
                    catch (...)
                    {
                        device.reset();
                        retry_at_100ns = now + 5'000'000LL;
                    }
                }

                if (device.audio_client && now >= device_check_at_100ns)
                {
                    try
                    {
                        winrt::com_ptr<IMMDevice> current = get_default_render_device(*enumerator);
                        if (get_device_id(*current) != device.id)
                        {
                            device.reset();
                            device_change_callback_();
                            retry_at_100ns = 0;
                        }

                        device_check_at_100ns = now + 5'000'000LL;
                    }
                    catch (...)
                    {
                        device.reset();
                        device_change_callback_();
                        retry_at_100ns = now + 5'000'000LL;
                    }
                }

                if (device.capture_client)
                {
                    const DWORD wait_result = WaitForSingleObject(device.ready_event.get(), 10);
                    if (wait_result == WAIT_FAILED)
                    {
                        winrt::throw_last_error();
                    }

                    if (wait_result == WAIT_OBJECT_0)
                    {
                        UINT32 packet_frames{};
                        while (SUCCEEDED(device.capture_client->GetNextPacketSize(&packet_frames)) &&
                            packet_frames > 0)
                        {
                            BYTE* data{};
                            UINT32 frames{};
                            DWORD flags{};
                            UINT64 device_position{};
                            UINT64 qpc_position{};
                            winrt::check_hresult(device.capture_client->GetBuffer(
                                &data,
                                &frames,
                                &flags,
                                &device_position,
                                &qpc_position));
                            try
                            {
                                packet_callback_();
                                const std::int64_t packet_start = frames_from_100ns(
                                    static_cast<std::int64_t>(qpc_position) - session_start_100ns_);
                                std::int64_t pending_end = emitted_frames +
                                    static_cast<std::int64_t>(pending_samples.size() / channels);
                                std::uint32_t skip_frames{};
                                if (packet_start > pending_end)
                                {
                                    const auto gap = static_cast<std::size_t>(std::min<std::int64_t>(
                                        packet_start - pending_end,
                                        static_cast<std::int64_t>(maximum_pending_frames)));
                                    pending_samples.insert(
                                        pending_samples.end(),
                                        gap * channels,
                                        0);
                                    timestamp_correction_callback_();
                                }
                                else if (packet_start < pending_end)
                                {
                                    skip_frames = static_cast<std::uint32_t>(std::min<std::int64_t>(
                                        pending_end - packet_start,
                                        frames));
                                    if (skip_frames > 0)
                                    {
                                        timestamp_correction_callback_();
                                    }
                                }

                                const std::uint32_t kept_frames = frames - skip_frames;
                                const std::size_t kept_samples =
                                    static_cast<std::size_t>(kept_frames) * channels;
                                if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0 || data == nullptr)
                                {
                                    pending_samples.insert(pending_samples.end(), kept_samples, 0);
                                }
                                else
                                {
                                    const auto* source = reinterpret_cast<const std::int16_t*>(data) +
                                        static_cast<std::size_t>(skip_frames) * channels;
                                    pending_samples.insert(
                                        pending_samples.end(),
                                        source,
                                        source + kept_samples);
                                }

                                const std::size_t maximum_samples = maximum_pending_frames * channels;
                                if (pending_samples.size() > maximum_samples)
                                {
                                    const std::size_t excess = pending_samples.size() - maximum_samples;
                                    pending_samples.erase(
                                        pending_samples.begin(),
                                        pending_samples.begin() + static_cast<std::ptrdiff_t>(excess));
                                    timestamp_correction_callback_();
                                }
                            }
                            catch (...)
                            {
                                device.capture_client->ReleaseBuffer(frames);
                                throw;
                            }

                            winrt::check_hresult(device.capture_client->ReleaseBuffer(frames));
                        }
                    }
                }
                else
                {
                    std::this_thread::sleep_for(std::chrono::milliseconds(10));
                }

                const std::int64_t target_frames = frames_from_100ns(
                    monotonic_time_100ns() - session_start_100ns_);
                while (emitted_frames + samples_per_frame <= target_frames && !stopping_.load())
                {
                    std::vector<std::int16_t> frame(
                        static_cast<std::size_t>(samples_per_frame) * channels,
                        0);
                    const std::size_t copy_samples = std::min(frame.size(), pending_samples.size());
                    for (std::size_t index = 0; index < copy_samples; ++index)
                    {
                        frame[index] = pending_samples.front();
                        pending_samples.pop_front();
                    }

                    pcm_callback_(std::move(frame), emitted_frames);
                    emitted_frames += samples_per_frame;
                }
            }

            device.reset();
            winrt::uninit_apartment();
            apartment_initialized = false;
        }
        catch (...)
        {
            if (apartment_initialized)
            {
                winrt::uninit_apartment();
            }

            try
            {
                failure_callback_(std::current_exception());
            }
            catch (...)
            {
            }
        }
    }
}
