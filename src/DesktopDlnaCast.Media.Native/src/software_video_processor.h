#pragma once

#include "ddc_media.h"

#include <d3d11.h>
#include <winrt/base.h>

#include <cstdint>
#include <mutex>
#include <vector>

struct SwsContext;

namespace ddc
{
    class software_video_processor final
    {
    public:
        software_video_processor(
            std::int32_t output_width,
            std::int32_t output_height,
            std::int32_t aspect_ratio_mode);
        software_video_processor(const software_video_processor&) = delete;
        software_video_processor& operator=(const software_video_processor&) = delete;
        ~software_video_processor();

        [[nodiscard]] std::vector<std::uint8_t> process(
            const winrt::com_ptr<ID3D11Texture2D>& bgra_texture,
            std::int32_t source_width,
            std::int32_t source_height);

    private:
        void ensure_resources(
            const winrt::com_ptr<ID3D11Texture2D>& texture,
            std::int32_t source_width,
            std::int32_t source_height,
            std::int32_t scaled_width,
            std::int32_t scaled_height);
        void close() noexcept;

        const std::int32_t output_width_;
        const std::int32_t output_height_;
        const std::int32_t aspect_ratio_mode_;
        std::mutex mutex_;
        std::int32_t source_width_{};
        std::int32_t source_height_{};
        std::int32_t scaled_width_{};
        std::int32_t scaled_height_{};
        winrt::com_ptr<ID3D11Device> device_;
        winrt::com_ptr<ID3D11DeviceContext> context_;
        winrt::com_ptr<ID3D11Texture2D> staging_texture_;
        SwsContext* scale_context_{};
    };
}
