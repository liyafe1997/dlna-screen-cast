#pragma once

#include <atomic>
#include <cstdint>
#include <exception>
#include <functional>
#include <thread>
#include <vector>

namespace ddc
{
    using audio_pcm_callback =
        std::function<void(std::vector<std::int16_t> pcm, std::int64_t start_sample)>;
    using audio_event_callback = std::function<void()>;
    using audio_failure_callback = std::function<void(std::exception_ptr failure)>;

    class wasapi_loopback_capture final
    {
    public:
        static constexpr std::int32_t sample_rate = 48'000;
        static constexpr std::int32_t channels = 2;
        static constexpr std::int32_t samples_per_frame = 1'024;

        wasapi_loopback_capture() = default;
        wasapi_loopback_capture(const wasapi_loopback_capture&) = delete;
        wasapi_loopback_capture& operator=(const wasapi_loopback_capture&) = delete;
        ~wasapi_loopback_capture();

        void start(
            std::int64_t session_start_100ns,
            bool mute_local_playback,
            audio_pcm_callback pcm_callback,
            audio_event_callback packet_callback,
            audio_event_callback device_change_callback,
            audio_event_callback timestamp_correction_callback,
            audio_event_callback local_mute_change_callback,
            audio_event_callback local_mute_restore_failure_callback,
            audio_failure_callback failure_callback);
        void stop() noexcept;

        [[nodiscard]] static std::int64_t monotonic_time_100ns();

    private:
        void run() noexcept;

        std::atomic<bool> stopping_{};
        std::thread worker_;
        std::int64_t session_start_100ns_{};
        bool mute_local_playback_{};
        audio_pcm_callback pcm_callback_;
        audio_event_callback packet_callback_;
        audio_event_callback device_change_callback_;
        audio_event_callback timestamp_correction_callback_;
        audio_event_callback local_mute_change_callback_;
        audio_event_callback local_mute_restore_failure_callback_;
        audio_failure_callback failure_callback_;
    };
}
