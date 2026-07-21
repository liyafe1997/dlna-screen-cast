#pragma once

#include "ddc_media.h"

#include <d3d11.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <winrt/base.h>

#include <condition_variable>
#include <cstdint>
#include <exception>
#include <functional>
#include <future>
#include <mutex>
#include <thread>

namespace ddc
{
    using captured_frame_callback = std::function<void(
        const winrt::com_ptr<ID3D11Texture2D>& texture,
        std::int32_t width,
        std::int32_t height,
        std::int64_t timestamp_100ns,
        bool is_repeated_frame)>;
    using capture_failure_callback = std::function<void(std::exception_ptr failure)>;

    class graphics_capture_source final
    {
    public:
        graphics_capture_source() = default;
        graphics_capture_source(const graphics_capture_source&) = delete;
        graphics_capture_source& operator=(const graphics_capture_source&) = delete;
        ~graphics_capture_source();

        void start(
            const ddc_stream_config& config,
            captured_frame_callback callback,
            capture_failure_callback failure_callback);
        void stop() noexcept;
        [[nodiscard]] std::exception_ptr failure() const noexcept;

    private:
        void run(
            ddc_stream_config config,
            captured_frame_callback callback,
            capture_failure_callback failure_callback,
            std::promise<void> started) noexcept;
        void initialize(
            const ddc_stream_config& config,
            captured_frame_callback callback,
            capture_failure_callback failure_callback);
        void close_on_capture_thread() noexcept;
        void on_frame_arrived(
            const winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool& sender,
            const winrt::Windows::Foundation::IInspectable& args) noexcept;
        void record_failure(std::exception_ptr failure) noexcept;
        [[nodiscard]] winrt::Windows::Graphics::Capture::GraphicsCaptureItem create_item(
            const ddc_stream_config& config) const;

        mutable std::mutex lifecycle_mutex_;
        std::mutex frame_mutex_;
        std::condition_variable stop_requested_;
        std::thread worker_;
        bool stopping_{};
        bool running_{};
        std::exception_ptr failure_;
        captured_frame_callback callback_;
        capture_failure_callback failure_callback_;

        winrt::com_ptr<ID3D11Device> d3d_device_;
        winrt::com_ptr<ID3D11DeviceContext> d3d_context_;
        winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice winrt_device_{ nullptr };
        winrt::Windows::Graphics::Capture::GraphicsCaptureItem item_{ nullptr };
        winrt::Windows::Graphics::Capture::Direct3D11CaptureFramePool frame_pool_{ nullptr };
        winrt::Windows::Graphics::Capture::GraphicsCaptureSession capture_session_{ nullptr };
        winrt::event_token frame_arrived_token_{};
        winrt::Windows::Graphics::SizeInt32 last_size_{};
        winrt::com_ptr<ID3D11Texture2D> latest_texture_;
        std::int32_t latest_width_{};
        std::int32_t latest_height_{};
        std::uint64_t latest_generation_{};
    };
}
