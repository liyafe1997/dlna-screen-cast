#include "software_video_processor.h"

#include "d3d11_video_processor.h"

extern "C"
{
#include <libswscale/swscale.h>
}

#include <algorithm>
#include <limits>
#include <stdexcept>

namespace ddc
{
    software_video_processor::software_video_processor(
        const std::int32_t output_width,
        const std::int32_t output_height,
        const std::int32_t aspect_ratio_mode)
        : output_width_(output_width),
          output_height_(output_height),
          aspect_ratio_mode_(aspect_ratio_mode)
    {
        if (output_width < 2 || output_height < 2 ||
            (output_width & 1) != 0 || (output_height & 1) != 0)
        {
            throw std::invalid_argument(
                "Software NV12 output dimensions must be positive and even.");
        }
    }

    software_video_processor::~software_video_processor()
    {
        close();
    }

    std::vector<std::uint8_t> software_video_processor::process(
        const winrt::com_ptr<ID3D11Texture2D>& bgra_texture,
        const std::int32_t source_width,
        const std::int32_t source_height)
    {
        if (!bgra_texture || source_width <= 0 || source_height <= 0)
        {
            throw std::invalid_argument("A valid BGRA capture texture is required.");
        }

        std::scoped_lock lock(mutex_);
        const video_layout layout = calculate_video_layout(
            source_width,
            source_height,
            output_width_,
            output_height_,
            aspect_ratio_mode_);
        const RECT& source = layout.source;
        const RECT& destination = layout.destination;
        const std::int32_t cropped_width = source.right - source.left;
        const std::int32_t cropped_height = source.bottom - source.top;
        const std::int32_t scaled_width = destination.right - destination.left;
        const std::int32_t scaled_height = destination.bottom - destination.top;
        ensure_resources(
            bgra_texture,
            cropped_width,
            cropped_height,
            scaled_width,
            scaled_height);

        context_->CopyResource(staging_texture_.get(), bgra_texture.get());
        D3D11_MAPPED_SUBRESOURCE mapped{};
        winrt::check_hresult(context_->Map(
            staging_texture_.get(),
            0,
            D3D11_MAP_READ,
            0,
            &mapped));

        const std::size_t y_bytes =
            static_cast<std::size_t>(output_width_) * output_height_;
        std::vector<std::uint8_t> output(y_bytes + y_bytes / 2U);
        std::fill_n(output.begin(), y_bytes, static_cast<std::uint8_t>(16));
        std::fill(output.begin() + static_cast<std::ptrdiff_t>(y_bytes), output.end(),
            static_cast<std::uint8_t>(128));
        constexpr std::size_t scaler_padding_bytes = 64;
        const std::int32_t scaled_stride = (scaled_width + 31) & ~31;
        const std::size_t scaled_y_bytes =
            static_cast<std::size_t>(scaled_stride) * scaled_height;
        const std::size_t scaled_uv_bytes = scaled_y_bytes / 2U;
        std::vector<std::uint8_t> scaled_frame(
            scaled_y_bytes + scaled_uv_bytes + scaler_padding_bytes);
        try
        {
            const std::uint8_t* source_planes[4]{
                static_cast<const std::uint8_t*>(mapped.pData) +
                    static_cast<std::size_t>(source.top) * mapped.RowPitch +
                    static_cast<std::size_t>(source.left) * 4U,
                nullptr,
                nullptr,
                nullptr,
            };
            if (mapped.RowPitch > static_cast<UINT>(std::numeric_limits<int>::max()))
            {
                throw std::overflow_error("The BGRA capture stride exceeds the scaler range.");
            }

            const int source_strides[4]{
                static_cast<int>(mapped.RowPitch),
                0,
                0,
                0,
            };
            std::uint8_t* destination_planes[4]{
                scaled_frame.data(),
                scaled_frame.data() + scaled_y_bytes,
                nullptr,
                nullptr,
            };
            const int destination_strides[4]{ scaled_stride, scaled_stride, 0, 0 };
            const int rows = sws_scale(
                scale_context_,
                source_planes,
                source_strides,
                0,
                cropped_height,
                destination_planes,
                destination_strides);
            if (rows != scaled_height)
            {
                throw std::runtime_error("FFmpeg libswscale did not produce the complete NV12 frame.");
            }

            for (std::int32_t row = 0; row < scaled_height; ++row)
            {
                std::copy_n(
                    scaled_frame.data() + static_cast<std::size_t>(row) * scaled_stride,
                    scaled_width,
                    output.data() +
                        static_cast<std::size_t>(destination.top + row) * output_width_ +
                        destination.left);
            }

            for (std::int32_t row = 0; row < scaled_height / 2; ++row)
            {
                std::copy_n(
                    scaled_frame.data() + scaled_y_bytes +
                        static_cast<std::size_t>(row) * scaled_stride,
                    scaled_width,
                    output.data() + y_bytes +
                        static_cast<std::size_t>(destination.top / 2 + row) * output_width_ +
                        destination.left);
            }

            context_->Unmap(staging_texture_.get(), 0);
        }
        catch (...)
        {
            context_->Unmap(staging_texture_.get(), 0);
            throw;
        }

        return output;
    }

    void software_video_processor::ensure_resources(
        const winrt::com_ptr<ID3D11Texture2D>& texture,
        const std::int32_t source_width,
        const std::int32_t source_height,
        const std::int32_t scaled_width,
        const std::int32_t scaled_height)
    {
        D3D11_TEXTURE2D_DESC source_description{};
        texture->GetDesc(&source_description);
        if (source_description.Format != DXGI_FORMAT_B8G8R8A8_UNORM ||
            source_description.Width < static_cast<UINT>(source_width) ||
            source_description.Height < static_cast<UINT>(source_height))
        {
            throw std::invalid_argument("The software processor input is not a matching BGRA texture.");
        }

        winrt::com_ptr<ID3D11Device> source_device;
        texture->GetDevice(source_device.put());
        const bool device_changed = !device_ || source_device.get() != device_.get();
        const bool source_changed =
            device_changed || source_width != source_width_ || source_height != source_height_;
        if (source_changed)
        {
            staging_texture_ = nullptr;
            context_ = nullptr;
            device_ = std::move(source_device);
            device_->GetImmediateContext(context_.put());
            D3D11_TEXTURE2D_DESC staging_description = source_description;
            staging_description.Usage = D3D11_USAGE_STAGING;
            staging_description.BindFlags = 0;
            staging_description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
            staging_description.MiscFlags = 0;
            winrt::check_hresult(device_->CreateTexture2D(
                &staging_description,
                nullptr,
                staging_texture_.put()));
            source_width_ = source_width;
            source_height_ = source_height;
        }

        if (source_changed || scaled_width != scaled_width_ ||
            scaled_height != scaled_height_ || !scale_context_)
        {
            sws_freeContext(scale_context_);
            scale_context_ = sws_getContext(
                source_width,
                source_height,
                AV_PIX_FMT_BGRA,
                scaled_width,
                scaled_height,
                AV_PIX_FMT_NV12,
                SWS_BILINEAR,
                nullptr,
                nullptr,
                nullptr);
            if (!scale_context_)
            {
                throw std::runtime_error("FFmpeg libswscale could not create the BGRA-to-NV12 scaler.");
            }

            const int* coefficients = sws_getCoefficients(SWS_CS_ITU709);
            if (sws_setColorspaceDetails(
                    scale_context_,
                    coefficients,
                    1,
                    coefficients,
                    0,
                    0,
                    1 << 16,
                    1 << 16) < 0)
            {
                throw std::runtime_error("FFmpeg libswscale rejected the BT.709 limited-range settings.");
            }

            scaled_width_ = scaled_width;
            scaled_height_ = scaled_height;
        }
    }

    void software_video_processor::close() noexcept
    {
        sws_freeContext(scale_context_);
        scale_context_ = nullptr;
        staging_texture_ = nullptr;
        context_ = nullptr;
        device_ = nullptr;
    }
}
