#pragma once

#include "ddc_media.h"

#include <d3d11.h>
#include <winrt/base.h>

#include <cstdint>
#include <mutex>
#include <vector>

namespace ddc
{
    struct video_layout
    {
        RECT source;
        RECT destination;
    };

    [[nodiscard]] video_layout calculate_video_layout(
        std::int32_t source_width,
        std::int32_t source_height,
        std::int32_t output_width,
        std::int32_t output_height,
        std::int32_t aspect_ratio_mode);

    class d3d11_video_processor final
    {
    public:
        d3d11_video_processor(
            std::int32_t output_width,
            std::int32_t output_height,
            std::int32_t frame_rate,
            std::int32_t aspect_ratio_mode);
        d3d11_video_processor(const d3d11_video_processor&) = delete;
        d3d11_video_processor& operator=(const d3d11_video_processor&) = delete;
        ~d3d11_video_processor() = default;

        [[nodiscard]] std::vector<std::uint8_t> process(
            const winrt::com_ptr<ID3D11Texture2D>& bgra_texture,
            std::int32_t source_width,
            std::int32_t source_height);

    private:
        void ensure_device(const winrt::com_ptr<ID3D11Texture2D>& texture);
        void recreate_processor(std::int32_t source_width, std::int32_t source_height);

        const std::int32_t output_width_;
        const std::int32_t output_height_;
        const std::int32_t frame_rate_;
        const std::int32_t aspect_ratio_mode_;
        std::mutex mutex_;
        std::int32_t source_width_{};
        std::int32_t source_height_{};
        winrt::com_ptr<ID3D11Device> device_;
        winrt::com_ptr<ID3D11DeviceContext> context_;
        winrt::com_ptr<ID3D11VideoDevice> video_device_;
        winrt::com_ptr<ID3D11VideoContext> video_context_;
        winrt::com_ptr<ID3D11VideoProcessorEnumerator> enumerator_;
        winrt::com_ptr<ID3D11VideoProcessor> processor_;
        winrt::com_ptr<ID3D11Texture2D> nv12_texture_;
        winrt::com_ptr<ID3D11Texture2D> staging_texture_;
        winrt::com_ptr<ID3D11VideoProcessorOutputView> output_view_;
    };
}
