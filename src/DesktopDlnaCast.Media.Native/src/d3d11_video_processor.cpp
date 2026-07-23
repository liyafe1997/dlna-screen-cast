#include "d3d11_video_processor.h"

#include <algorithm>
#include <cstring>
#include <stdexcept>

namespace ddc
{
    video_layout calculate_video_layout(
        const std::int32_t source_width,
        const std::int32_t source_height,
        const std::int32_t output_width,
        const std::int32_t output_height,
        const std::int32_t aspect_ratio_mode)
    {
        if (source_width <= 0 || source_height <= 0 || output_width <= 0 || output_height <= 0)
        {
            throw std::invalid_argument("Video layout dimensions must be positive.");
        }

        const RECT full_source{ 0, 0, source_width, source_height };
        const RECT full_destination{ 0, 0, output_width, output_height };
        if (aspect_ratio_mode == DDC_ASPECT_RATIO_STRETCH)
        {
            return { full_source, full_destination };
        }

        if (aspect_ratio_mode == DDC_ASPECT_RATIO_CENTER_CROP)
        {
            std::int32_t crop_width = source_width;
            std::int32_t crop_height = static_cast<std::int32_t>(
                static_cast<std::int64_t>(source_width) * output_height / output_width);
            if (crop_height > source_height)
            {
                crop_height = source_height;
                crop_width = static_cast<std::int32_t>(
                    static_cast<std::int64_t>(source_height) * output_width / output_height);
            }

            crop_width = std::max(2, crop_width & ~1);
            crop_height = std::max(2, crop_height & ~1);
            const std::int32_t left = ((source_width - crop_width) / 2) & ~1;
            const std::int32_t top = ((source_height - crop_height) / 2) & ~1;
            return { { left, top, left + crop_width, top + crop_height }, full_destination };
        }

        const std::int64_t width_limited_height =
            static_cast<std::int64_t>(output_width) * source_height / source_width;
        std::int32_t scaled_width{};
        std::int32_t scaled_height{};
        if (width_limited_height <= output_height)
        {
            scaled_width = output_width;
            scaled_height = static_cast<std::int32_t>(width_limited_height);
        }
        else
        {
            scaled_height = output_height;
            scaled_width = static_cast<std::int32_t>(
                static_cast<std::int64_t>(output_height) * source_width / source_height);
        }

        scaled_width = std::max(2, scaled_width & ~1);
        scaled_height = std::max(2, scaled_height & ~1);
        const std::int32_t left = ((output_width - scaled_width) / 2) & ~1;
        const std::int32_t top = ((output_height - scaled_height) / 2) & ~1;
        return { full_source, { left, top, left + scaled_width, top + scaled_height } };
    }

    d3d11_video_processor::d3d11_video_processor(
        const std::int32_t output_width,
        const std::int32_t output_height,
        const std::int32_t frame_rate,
        const std::int32_t aspect_ratio_mode)
        : output_width_(output_width),
          output_height_(output_height),
          frame_rate_(frame_rate),
          aspect_ratio_mode_(aspect_ratio_mode)
    {
        if (output_width < 2 || output_height < 2 ||
            (output_width & 1) != 0 || (output_height & 1) != 0 ||
            frame_rate < 1 || frame_rate > 30)
        {
            throw std::invalid_argument("NV12 output dimensions must be positive and even.");
        }
    }

    std::vector<std::uint8_t> d3d11_video_processor::process(
        const winrt::com_ptr<ID3D11Texture2D>& bgra_texture,
        const std::int32_t source_width,
        const std::int32_t source_height)
    {
        if (!bgra_texture || source_width <= 0 || source_height <= 0)
        {
            throw std::invalid_argument("A valid BGRA capture texture is required.");
        }

        std::scoped_lock lock(mutex_);
        ensure_device(bgra_texture);
        if (source_width != source_width_ || source_height != source_height_)
        {
            recreate_processor(source_width, source_height);
        }

        D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC input_description{};
        input_description.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
        input_description.Texture2D.MipSlice = 0;
        input_description.Texture2D.ArraySlice = 0;
        winrt::com_ptr<ID3D11VideoProcessorInputView> input_view;
        winrt::check_hresult(video_device_->CreateVideoProcessorInputView(
            bgra_texture.get(),
            enumerator_.get(),
            &input_description,
            input_view.put()));

        const video_layout layout = calculate_video_layout(
            source_width,
            source_height,
            output_width_,
            output_height_,
            aspect_ratio_mode_);
        video_context_->VideoProcessorSetStreamFrameFormat(
            processor_.get(),
            0,
            D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
        video_context_->VideoProcessorSetStreamSourceRect(processor_.get(), 0, TRUE, &layout.source);
        video_context_->VideoProcessorSetStreamDestRect(processor_.get(), 0, TRUE, &layout.destination);
        const RECT output_rect{ 0, 0, output_width_, output_height_ };
        video_context_->VideoProcessorSetOutputTargetRect(processor_.get(), TRUE, &output_rect);

        D3D11_VIDEO_PROCESSOR_STREAM stream{};
        stream.Enable = TRUE;
        stream.OutputIndex = 0;
        stream.InputFrameOrField = 0;
        stream.PastFrames = 0;
        stream.FutureFrames = 0;
        stream.pInputSurface = input_view.get();
        winrt::check_hresult(video_context_->VideoProcessorBlt(
            processor_.get(),
            output_view_.get(),
            0,
            1,
            &stream));

        context_->CopyResource(staging_texture_.get(), nv12_texture_.get());
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
        try
        {
            const auto* source = static_cast<const std::uint8_t*>(mapped.pData);
            for (std::int32_t row = 0; row < output_height_; ++row)
            {
                std::memcpy(
                    output.data() + static_cast<std::size_t>(row) * output_width_,
                    source + static_cast<std::size_t>(row) * mapped.RowPitch,
                    static_cast<std::size_t>(output_width_));
            }

            const auto* source_uv =
                source + static_cast<std::size_t>(mapped.RowPitch) * output_height_;
            auto* destination_uv = output.data() + y_bytes;
            for (std::int32_t row = 0; row < output_height_ / 2; ++row)
            {
                std::memcpy(
                    destination_uv + static_cast<std::size_t>(row) * output_width_,
                    source_uv + static_cast<std::size_t>(row) * mapped.RowPitch,
                    static_cast<std::size_t>(output_width_));
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

    void d3d11_video_processor::ensure_device(const winrt::com_ptr<ID3D11Texture2D>& texture)
    {
        winrt::com_ptr<ID3D11Device> source_device;
        texture->GetDevice(source_device.put());
        if (device_)
        {
            if (source_device.get() != device_.get())
            {
                throw std::invalid_argument("Capture textures changed D3D11 devices during a session.");
            }

            return;
        }

        device_ = std::move(source_device);
        device_->GetImmediateContext(context_.put());
        video_device_ = device_.try_as<ID3D11VideoDevice>();
        if (!video_device_)
        {
            throw std::runtime_error(
                "The capture D3D11 device does not expose ID3D11VideoDevice for BGRA-to-NV12 conversion.");
        }

        video_context_ = context_.try_as<ID3D11VideoContext>();
        if (!video_context_)
        {
            throw std::runtime_error(
                "The capture D3D11 context does not expose ID3D11VideoContext for BGRA-to-NV12 conversion.");
        }

        D3D11_TEXTURE2D_DESC output_description{};
        output_description.Width = static_cast<UINT>(output_width_);
        output_description.Height = static_cast<UINT>(output_height_);
        output_description.MipLevels = 1;
        output_description.ArraySize = 1;
        output_description.Format = DXGI_FORMAT_NV12;
        output_description.SampleDesc.Count = 1;
        output_description.Usage = D3D11_USAGE_DEFAULT;
        output_description.BindFlags = D3D11_BIND_RENDER_TARGET;
        winrt::check_hresult(device_->CreateTexture2D(
            &output_description,
            nullptr,
            nv12_texture_.put()));
        D3D11_TEXTURE2D_DESC staging_description = output_description;
        staging_description.Usage = D3D11_USAGE_STAGING;
        staging_description.BindFlags = 0;
        staging_description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        winrt::check_hresult(device_->CreateTexture2D(
            &staging_description,
            nullptr,
            staging_texture_.put()));
    }

    void d3d11_video_processor::recreate_processor(
        const std::int32_t source_width,
        const std::int32_t source_height)
    {
        D3D11_VIDEO_PROCESSOR_CONTENT_DESC content{};
        content.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
        content.InputFrameRate.Numerator = static_cast<UINT>(frame_rate_);
        content.InputFrameRate.Denominator = 1;
        content.InputWidth = static_cast<UINT>(source_width);
        content.InputHeight = static_cast<UINT>(source_height);
        content.OutputFrameRate.Numerator = static_cast<UINT>(frame_rate_);
        content.OutputFrameRate.Denominator = 1;
        content.OutputWidth = static_cast<UINT>(output_width_);
        content.OutputHeight = static_cast<UINT>(output_height_);
        content.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
        enumerator_ = nullptr;
        processor_ = nullptr;
        output_view_ = nullptr;
        winrt::check_hresult(video_device_->CreateVideoProcessorEnumerator(
            &content,
            enumerator_.put()));

        UINT input_flags{};
        winrt::check_hresult(enumerator_->CheckVideoProcessorFormat(
            DXGI_FORMAT_B8G8R8A8_UNORM,
            &input_flags));
        UINT output_flags{};
        winrt::check_hresult(enumerator_->CheckVideoProcessorFormat(
            DXGI_FORMAT_NV12,
            &output_flags));
        if ((input_flags & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT) == 0 ||
            (output_flags & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT) == 0)
        {
            throw std::runtime_error("The D3D11 video processor cannot convert BGRA to NV12.");
        }

        winrt::check_hresult(video_device_->CreateVideoProcessor(
            enumerator_.get(),
            0,
            processor_.put()));
        D3D11_VIDEO_PROCESSOR_COLOR_SPACE input_color_space{};
        input_color_space.Usage = 1;
        input_color_space.RGB_Range = 0;
        video_context_->VideoProcessorSetStreamColorSpace(
            processor_.get(),
            0,
            &input_color_space);
        D3D11_VIDEO_PROCESSOR_COLOR_SPACE output_color_space{};
        output_color_space.Usage = 1;
        output_color_space.YCbCr_Matrix = 1;
        output_color_space.Nominal_Range = static_cast<UINT>(
            D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_16_235);
        video_context_->VideoProcessorSetOutputColorSpace(
            processor_.get(),
            &output_color_space);
        D3D11_VIDEO_COLOR black{};
        black.YCbCr.Y = 16.0F / 255.0F;
        black.YCbCr.Cb = 0.5F;
        black.YCbCr.Cr = 0.5F;
        black.YCbCr.A = 1.0F;
        video_context_->VideoProcessorSetOutputBackgroundColor(
            processor_.get(),
            TRUE,
            &black);
        D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC output_view_description{};
        output_view_description.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
        output_view_description.Texture2D.MipSlice = 0;
        winrt::check_hresult(video_device_->CreateVideoProcessorOutputView(
            nv12_texture_.get(),
            enumerator_.get(),
            &output_view_description,
            output_view_.put()));
        source_width_ = source_width;
        source_height_ = source_height;
    }
}
