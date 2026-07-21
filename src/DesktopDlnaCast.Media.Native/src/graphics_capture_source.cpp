#include "graphics_capture_source.h"

#include <windows.graphics.capture.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>

#include <d3d11_4.h>
#include <dxgi.h>
#include <chrono>
#include <stdexcept>
#include <utility>

namespace
{
    [[nodiscard]] winrt::com_ptr<ID3D11Device> create_d3d_device()
    {
        const auto try_create = [](const D3D_DRIVER_TYPE driver_type, const UINT flags)
        {
            winrt::com_ptr<ID3D11Device> candidate;
            D3D_FEATURE_LEVEL selected_feature_level{};
            const HRESULT result = D3D11CreateDevice(
                nullptr,
                driver_type,
                nullptr,
                flags,
                nullptr,
                0,
                D3D11_SDK_VERSION,
                candidate.put(),
                &selected_feature_level,
                nullptr);
            if (FAILED(result))
            {
                return winrt::com_ptr<ID3D11Device>{};
            }

            return candidate;
        };

        constexpr UINT video_flags =
            D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
        constexpr UINT basic_flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        auto device = try_create(D3D_DRIVER_TYPE_HARDWARE, video_flags);
        if (!device)
        {
            device = try_create(D3D_DRIVER_TYPE_HARDWARE, basic_flags);
        }

        if (!device)
        {
            device = try_create(D3D_DRIVER_TYPE_WARP, basic_flags);
        }

        if (!device)
        {
            throw std::runtime_error(
                "Neither the default adapter nor WARP could create a BGRA-capable D3D11 capture device.");
        }

        auto multithread = device.as<ID3D11Multithread>();
        multithread->SetMultithreadProtected(TRUE);
        return device;
    }

    [[nodiscard]] winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice
    create_winrt_device(const winrt::com_ptr<ID3D11Device>& device)
    {
        auto dxgi_device = device.as<IDXGIDevice>();
        winrt::com_ptr<IInspectable> inspectable;
        winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi_device.get(), inspectable.put()));
        return inspectable.as<winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice>();
    }

    [[nodiscard]] winrt::com_ptr<ID3D11Texture2D> unwrap_texture(
        const winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DSurface& surface)
    {
        using interface_access =
            Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess;
        auto access = surface.as<interface_access>();
        winrt::com_ptr<ID3D11Texture2D> texture;
        winrt::check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D), texture.put_void()));
        return texture;
    }
}

namespace ddc
{
    graphics_capture_source::~graphics_capture_source()
    {
        stop();
    }

    void graphics_capture_source::start(
        const ddc_stream_config& config,
        captured_frame_callback callback,
        capture_failure_callback failure_callback)
    {
        if (!callback || !failure_callback)
        {
            throw std::invalid_argument("Graphics capture frame and failure callbacks are required.");
        }

        std::future<void> ready;
        {
            std::scoped_lock lock(lifecycle_mutex_);
            if (running_ || worker_.joinable())
            {
                throw std::logic_error("The graphics capture source is already active.");
            }

            stopping_ = false;
            failure_ = nullptr;
            std::promise<void> started;
            ready = started.get_future();
            worker_ = std::thread(
                &graphics_capture_source::run,
                this,
                config,
                std::move(callback),
                std::move(failure_callback),
                std::move(started));
        }

        try
        {
            ready.get();
        }
        catch (...)
        {
            if (worker_.joinable())
            {
                worker_.join();
            }

            throw;
        }
    }

    void graphics_capture_source::stop() noexcept
    {
        try
        {
            {
                std::scoped_lock lock(lifecycle_mutex_);
                stopping_ = true;
            }

            stop_requested_.notify_all();
            if (worker_.joinable() && worker_.get_id() != std::this_thread::get_id())
            {
                worker_.join();
            }
        }
        catch (...)
        {
            record_failure(std::current_exception());
        }
    }

    std::exception_ptr graphics_capture_source::failure() const noexcept
    {
        try
        {
            std::scoped_lock lock(lifecycle_mutex_);
            return failure_;
        }
        catch (...)
        {
            return nullptr;
        }
    }

    void graphics_capture_source::run(
        const ddc_stream_config config,
        captured_frame_callback callback,
        capture_failure_callback failure_callback,
        std::promise<void> started) noexcept
    {
        bool apartment_initialized = false;
        try
        {
            winrt::init_apartment(winrt::apartment_type::multi_threaded);
            apartment_initialized = true;
            initialize(config, std::move(callback), std::move(failure_callback));
            {
                std::scoped_lock lock(lifecycle_mutex_);
                running_ = true;
            }

            started.set_value();
            const auto frame_interval = std::chrono::nanoseconds(
                1'000'000'000LL / config.frame_rate);
            auto next_frame_time = std::chrono::steady_clock::now();
            std::uint64_t emitted_generation{};
            while (true)
            {
                {
                    std::unique_lock lock(lifecycle_mutex_);
                    if (stop_requested_.wait_until(
                            lock,
                            next_frame_time,
                            [this] { return stopping_; }))
                    {
                        break;
                    }
                }

                winrt::com_ptr<ID3D11Texture2D> texture;
                std::int32_t width{};
                std::int32_t height{};
                std::uint64_t generation{};
                {
                    std::scoped_lock frame_lock(frame_mutex_);
                    texture = latest_texture_;
                    width = latest_width_;
                    height = latest_height_;
                    generation = latest_generation_;
                }

                if (texture && callback_)
                {
                    const auto timestamp_100ns =
                        std::chrono::duration_cast<std::chrono::nanoseconds>(
                            std::chrono::steady_clock::now().time_since_epoch()).count() / 100;
                    const bool is_repeated_frame = generation == emitted_generation;
                    callback_(texture, width, height, timestamp_100ns, is_repeated_frame);
                    emitted_generation = generation;
                }

                next_frame_time += frame_interval;
                const auto now = std::chrono::steady_clock::now();
                if (next_frame_time + frame_interval < now)
                {
                    next_frame_time = now + frame_interval;
                }
            }
        }
        catch (...)
        {
            std::exception_ptr current = std::current_exception();
            record_failure(current);
            try
            {
                started.set_exception(current);
            }
            catch (...)
            {
            }
        }

        close_on_capture_thread();
        if (apartment_initialized)
        {
            winrt::uninit_apartment();
        }

        std::scoped_lock lock(lifecycle_mutex_);
        running_ = false;
    }

    void graphics_capture_source::initialize(
        const ddc_stream_config& config,
        captured_frame_callback callback,
        capture_failure_callback failure_callback)
    {
        callback_ = std::move(callback);
        failure_callback_ = std::move(failure_callback);
        d3d_device_ = create_d3d_device();
        d3d_device_->GetImmediateContext(d3d_context_.put());
        winrt_device_ = create_winrt_device(d3d_device_);
        item_ = create_item(config);
        last_size_ = item_.Size();
        if (last_size_.Width <= 0 || last_size_.Height <= 0)
        {
            throw std::runtime_error("The capture source has an invalid size.");
        }

        frame_pool_ = winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool::CreateFreeThreaded(
            winrt_device_,
            winrt::Windows::Graphics::DirectX::DirectXPixelFormat::B8G8R8A8UIntNormalized,
            2,
            last_size_);
        capture_session_ = frame_pool_.CreateCaptureSession(item_);
        capture_session_.IsCursorCaptureEnabled(config.include_cursor != 0);
        frame_arrived_token_ = frame_pool_.FrameArrived(
            { this, &graphics_capture_source::on_frame_arrived });
        capture_session_.StartCapture();
    }

    void graphics_capture_source::close_on_capture_thread() noexcept
    {
        std::scoped_lock frame_lock(frame_mutex_);
        try
        {
            if (frame_pool_)
            {
                frame_pool_.FrameArrived(frame_arrived_token_);
            }

            if (capture_session_)
            {
                capture_session_.Close();
            }

            if (frame_pool_)
            {
                frame_pool_.Close();
            }
        }
        catch (...)
        {
            record_failure(std::current_exception());
        }

        callback_ = nullptr;
        failure_callback_ = nullptr;
        latest_texture_ = nullptr;
        latest_width_ = 0;
        latest_height_ = 0;
        latest_generation_ = 0;
        capture_session_ = nullptr;
        frame_pool_ = nullptr;
        item_ = nullptr;
        winrt_device_ = nullptr;
        d3d_context_ = nullptr;
        d3d_device_ = nullptr;
    }

    void graphics_capture_source::on_frame_arrived(
        const winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool& sender,
        const winrt::Windows::Foundation::IInspectable&) noexcept
    {
        std::scoped_lock frame_lock(frame_mutex_);
        try
        {
            auto frame = sender.TryGetNextFrame();
            if (!frame)
            {
                return;
            }

            const auto size = frame.ContentSize();
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            if (size.Width != last_size_.Width || size.Height != last_size_.Height)
            {
                last_size_ = size;
                latest_texture_ = nullptr;
                latest_width_ = 0;
                latest_height_ = 0;
                frame.Close();
                frame_pool_.Recreate(
                    winrt_device_,
                    winrt::Windows::Graphics::DirectX::DirectXPixelFormat::B8G8R8A8UIntNormalized,
                    2,
                    last_size_);
                return;
            }

            const auto source_texture = unwrap_texture(frame.Surface());
            D3D11_TEXTURE2D_DESC source_description{};
            source_texture->GetDesc(&source_description);
            if (!latest_texture_ || latest_width_ != size.Width || latest_height_ != size.Height)
            {
                source_description.Usage = D3D11_USAGE_DEFAULT;
                source_description.CPUAccessFlags = 0;
                source_description.MiscFlags = 0;
                winrt::check_hresult(d3d_device_->CreateTexture2D(
                    &source_description,
                    nullptr,
                    latest_texture_.put()));
            }

            d3d_context_->CopyResource(latest_texture_.get(), source_texture.get());
            latest_width_ = size.Width;
            latest_height_ = size.Height;
            ++latest_generation_;
        }
        catch (...)
        {
            record_failure(std::current_exception());
            {
                std::scoped_lock lock(lifecycle_mutex_);
                stopping_ = true;
            }

            stop_requested_.notify_all();
        }
    }

    void graphics_capture_source::record_failure(std::exception_ptr failure) noexcept
    {
        try
        {
            capture_failure_callback failure_callback;
            {
                std::scoped_lock lock(lifecycle_mutex_);
                if (!failure_)
                {
                    failure_ = failure;
                }

                failure_callback = failure_callback_;
            }

            if (failure_callback)
            {
                failure_callback(std::move(failure));
            }
        }
        catch (...)
        {
        }
    }

    winrt::Windows::Graphics::Capture::GraphicsCaptureItem
    graphics_capture_source::create_item(const ddc_stream_config& config) const
    {
        auto interop = winrt::get_activation_factory<
            winrt::Windows::Graphics::Capture::GraphicsCaptureItem,
            IGraphicsCaptureItemInterop>();
        winrt::Windows::Graphics::Capture::GraphicsCaptureItem item{ nullptr };
        HRESULT result{};
        if (config.source_kind == DDC_CAPTURE_SOURCE_DISPLAY)
        {
            auto monitor = reinterpret_cast<HMONITOR>(static_cast<std::uintptr_t>(config.source_handle));
            result = interop->CreateForMonitor(
                monitor,
                winrt::guid_of<ABI::Windows::Graphics::Capture::IGraphicsCaptureItem>(),
                reinterpret_cast<void**>(winrt::put_abi(item)));
        }
        else
        {
            auto window = reinterpret_cast<HWND>(static_cast<std::uintptr_t>(config.source_handle));
            result = interop->CreateForWindow(
                window,
                winrt::guid_of<ABI::Windows::Graphics::Capture::IGraphicsCaptureItem>(),
                reinterpret_cast<void**>(winrt::put_abi(item)));
        }

        winrt::check_hresult(result);
        return item;
    }
}
