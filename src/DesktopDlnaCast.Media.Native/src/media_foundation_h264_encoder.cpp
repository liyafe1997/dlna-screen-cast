#include "media_foundation_h264_encoder.h"

extern "C"
{
#include <libavutil/error.h>
#include <libavutil/opt.h>
#include <libavutil/pixfmt.h>
}

#include <mfapi.h>
#include <mftransform.h>

#include <algorithm>
#include <cstddef>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <utility>

namespace
{
    [[nodiscard]] std::string describe_ffmpeg_error(const int result)
    {
        char buffer[AV_ERROR_MAX_STRING_SIZE]{};
        if (av_strerror(result, buffer, sizeof(buffer)) < 0)
        {
            return "FFmpeg error " + std::to_string(result);
        }

        return buffer;
    }

    [[noreturn]] void throw_ffmpeg_error(const int result, const char* operation)
    {
        throw std::runtime_error(
            std::string("FFmpeg h264_mf failed while ") + operation + ": " +
            describe_ffmpeg_error(result) + ".");
    }

    void set_private_option(AVCodecContext& context, const char* name, const char* value)
    {
        const int result = av_opt_set(context.priv_data, name, value, 0);
        if (result < 0)
        {
            throw_ffmpeg_error(result, name);
        }
    }

    [[nodiscard]] bool has_hardware_h264_encoder()
    {
        const HRESULT startup_result = MFStartup(MF_VERSION, MFSTARTUP_LITE);
        if (FAILED(startup_result))
        {
            return false;
        }

        IMFActivate** activations{};
        UINT32 activation_count{};
        const MFT_REGISTER_TYPE_INFO output_type{ MFMediaType_Video, MFVideoFormat_H264 };
        const HRESULT enumeration_result = MFTEnumEx(
            MFT_CATEGORY_VIDEO_ENCODER,
            MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG_SORTANDFILTER,
            nullptr,
            &output_type,
            &activations,
            &activation_count);
        if (activations != nullptr)
        {
            for (UINT32 index = 0; index < activation_count; ++index)
            {
                activations[index]->Release();
            }

            CoTaskMemFree(activations);
        }

        MFShutdown();
        return SUCCEEDED(enumeration_result) && activation_count > 0;
    }
}

namespace ddc
{
    media_foundation_h264_encoder::media_foundation_h264_encoder(
        const std::int32_t width,
        const std::int32_t height,
        const std::int32_t frame_rate,
        const std::int32_t bitrate,
        const std::int32_t gop_frames,
        h264_sample_callback callback,
        const h264_encoder_preference preference)
        : width_(width),
          height_(height),
          frame_rate_(frame_rate),
          bitrate_(bitrate),
          gop_frames_(gop_frames),
          frame_duration_100ns_(10'000'000LL / frame_rate),
          callback_(std::move(callback))
    {
        if (width < 2 || height < 2 || (width & 1) != 0 || (height & 1) != 0 ||
            frame_rate < 1 || frame_rate > 30 || bitrate <= 0 || gop_frames <= 0 || !callback_)
        {
            throw std::invalid_argument("The H.264 encoder configuration is invalid.");
        }

        codec_ = avcodec_find_encoder_by_name("h264_mf");
        if (!codec_)
        {
            throw std::runtime_error(
                "The pinned FFmpeg build does not contain the Media Foundation h264_mf encoder.");
        }

        const bool opened_hardware =
            preference == h264_encoder_preference::hardware_preferred && try_open_encoder(true);
        if (!opened_hardware && !try_open_encoder(false))
        {
            throw std::runtime_error(
                "Neither a hardware nor the Windows software Media Foundation H.264 encoder "
                "accepted the requested NV12 profile.");
        }

        frame_ = av_frame_alloc();
        packet_ = av_packet_alloc();
        if (!frame_ || !packet_)
        {
            close();
            throw std::bad_alloc();
        }

        frame_->format = AV_PIX_FMT_NV12;
        frame_->width = width_;
        frame_->height = height_;
        const int buffer_result = av_frame_get_buffer(frame_, 32);
        if (buffer_result < 0)
        {
            close();
            throw_ffmpeg_error(buffer_result, "allocating an NV12 input frame");
        }

        update_codec_configuration();
        diagnostics_.accepted_width = codec_context_->width;
        diagnostics_.accepted_height = codec_context_->height;
        diagnostics_.frame_rate_numerator = codec_context_->framerate.num;
        diagnostics_.frame_rate_denominator = codec_context_->framerate.den;
        diagnostics_.accepted_video_bitrate = static_cast<std::int32_t>(std::clamp<std::int64_t>(
            codec_context_->bit_rate,
            0,
            std::numeric_limits<std::int32_t>::max()));
        diagnostics_.h264_profile = codec_context_->profile;
        diagnostics_.accepted_gop_frames = codec_context_->gop_size;
        diagnostics_.accepted_b_frame_count = codec_context_->max_b_frames;
    }

    media_foundation_h264_encoder::~media_foundation_h264_encoder()
    {
        close();
    }

    bool media_foundation_h264_encoder::try_open_encoder(const bool hardware)
    {
        // FFmpeg 8.1's h264_mf cleanup path uninitializes Media Foundation twice when a
        // forced hardware lookup finds no matching MFT. Avoid entering that failed path;
        // it can unbalance COM and later corrupt teardown when AAC is used in the same session.
        if (hardware && !has_hardware_h264_encoder())
        {
            hardware_fallback_reason_ = "No hardware H.264 Media Foundation encoder is registered";
            return false;
        }

        AVCodecContext* candidate = avcodec_alloc_context3(codec_);
        if (!candidate)
        {
            throw std::bad_alloc();
        }

        try
        {
            configure_context(*candidate, hardware);
            const int result = avcodec_open2(candidate, codec_, nullptr);
            if (result < 0)
            {
                if (hardware)
                {
                    hardware_fallback_reason_ = describe_ffmpeg_error(result);
                }

                avcodec_free_context(&candidate);
                return false;
            }

            codec_context_ = candidate;
            diagnostics_.is_hardware = hardware;
            encoder_name_ = hardware
                ? "Media Foundation hardware H.264 encoder (FFmpeg h264_mf)"
                : "Microsoft software H.264 encoder (FFmpeg h264_mf)";
            if (!hardware && !hardware_fallback_reason_.empty())
            {
                encoder_name_ += "; hardware fallback: " + hardware_fallback_reason_;
            }

            return true;
        }
        catch (...)
        {
            avcodec_free_context(&candidate);
            throw;
        }
    }

    void media_foundation_h264_encoder::configure_context(
        AVCodecContext& context,
        const bool hardware)
    {
        context.codec_type = AVMEDIA_TYPE_VIDEO;
        context.codec_id = AV_CODEC_ID_H264;
        context.width = width_;
        context.height = height_;
        context.pix_fmt = AV_PIX_FMT_NV12;
        context.time_base = { 1, 10'000'000 };
        context.framerate = { frame_rate_, 1 };
        context.bit_rate = bitrate_;
        context.rc_max_rate = bitrate_;
        context.gop_size = gop_frames_;
        context.max_b_frames = 0;
        context.profile = AV_PROFILE_H264_MAIN;
        context.flags |= AV_CODEC_FLAG_GLOBAL_HEADER | AV_CODEC_FLAG_LOW_DELAY;
        set_private_option(context, "hw_encoding", hardware ? "1" : "0");
        set_private_option(context, "rate_control", "cbr");
        set_private_option(context, "scenario", "live_streaming");
    }

    void media_foundation_h264_encoder::encode(
        const std::span<const std::uint8_t> nv12_frame,
        const std::int64_t timestamp_100ns)
    {
        if (closed_ || draining_ || !codec_context_ || !frame_ || !packet_)
        {
            throw std::logic_error("The H.264 encoder is not accepting input.");
        }

        const std::size_t y_bytes = static_cast<std::size_t>(width_) * height_;
        if (nv12_frame.size() != y_bytes + y_bytes / 2U || timestamp_100ns < 0)
        {
            throw std::invalid_argument("A complete NV12 frame and non-negative timestamp are required.");
        }

        const int writable_result = av_frame_make_writable(frame_);
        if (writable_result < 0)
        {
            throw_ffmpeg_error(writable_result, "making the NV12 input frame writable");
        }

        for (std::int32_t row = 0; row < height_; ++row)
        {
            std::memcpy(
                frame_->data[0] + static_cast<std::ptrdiff_t>(row) * frame_->linesize[0],
                nv12_frame.data() + static_cast<std::size_t>(row) * width_,
                static_cast<std::size_t>(width_));
        }

        const std::uint8_t* source_uv = nv12_frame.data() + y_bytes;
        for (std::int32_t row = 0; row < height_ / 2; ++row)
        {
            std::memcpy(
                frame_->data[1] + static_cast<std::ptrdiff_t>(row) * frame_->linesize[1],
                source_uv + static_cast<std::size_t>(row) * width_,
                static_cast<std::size_t>(width_));
        }

        frame_->pts = timestamp_100ns;
        frame_->duration = frame_duration_100ns_;
        const std::int64_t gop_duration_100ns =
            static_cast<std::int64_t>(gop_frames_) * 10'000'000LL / frame_rate_;
        const bool request_keyframe = last_requested_keyframe_timestamp_100ns_ < 0 ||
            timestamp_100ns - last_requested_keyframe_timestamp_100ns_ >= gop_duration_100ns;
        frame_->pict_type = request_keyframe
            ? AV_PICTURE_TYPE_I
            : AV_PICTURE_TYPE_NONE;

        int send_result = avcodec_send_frame(codec_context_, frame_);
        if (send_result == AVERROR(EAGAIN))
        {
            receive_available_packets(false);
            send_result = avcodec_send_frame(codec_context_, frame_);
        }

        if (send_result < 0)
        {
            throw_ffmpeg_error(send_result, "submitting an NV12 frame");
        }

        if (request_keyframe)
        {
            last_requested_keyframe_timestamp_100ns_ = timestamp_100ns;
        }

        receive_available_packets(false);
    }

    void media_foundation_h264_encoder::receive_available_packets(const bool draining)
    {
        while (true)
        {
            const int result = avcodec_receive_packet(codec_context_, packet_);
            if (result == AVERROR(EAGAIN) || result == AVERROR_EOF)
            {
                if (draining && result == AVERROR(EAGAIN))
                {
                    throw std::runtime_error(
                        "FFmpeg h264_mf requested more input while the encoder was draining.");
                }

                return;
            }

            if (result < 0)
            {
                throw_ffmpeg_error(result, "receiving encoded H.264 output");
            }

            try
            {
                h264_encoded_sample sample;
                sample.bytes.assign(packet_->data, packet_->data + packet_->size);
                sample.timestamp_100ns = packet_->pts == AV_NOPTS_VALUE
                    ? packet_->dts
                    : packet_->pts;
                if (sample.timestamp_100ns == AV_NOPTS_VALUE)
                {
                    sample.timestamp_100ns = 0;
                }

                sample.duration_100ns = packet_->duration > 0
                    ? packet_->duration
                    : frame_duration_100ns_;
                sample.key_frame = (packet_->flags & AV_PKT_FLAG_KEY) != 0;
                av_packet_unref(packet_);
                callback_(std::move(sample));
            }
            catch (...)
            {
                av_packet_unref(packet_);
                throw;
            }
        }
    }

    void media_foundation_h264_encoder::drain()
    {
        if (closed_ || draining_ || !codec_context_)
        {
            return;
        }

        draining_ = true;
        const int result = avcodec_send_frame(codec_context_, nullptr);
        if (result < 0 && result != AVERROR_EOF)
        {
            throw_ffmpeg_error(result, "starting encoder drain");
        }

        receive_available_packets(true);
    }

    void media_foundation_h264_encoder::update_codec_configuration()
    {
        codec_configuration_.clear();
        if (codec_context_->extradata && codec_context_->extradata_size > 0)
        {
            codec_configuration_.assign(
                codec_context_->extradata,
                codec_context_->extradata + codec_context_->extradata_size);
        }
    }

    const std::vector<std::uint8_t>&
    media_foundation_h264_encoder::codec_configuration() const noexcept
    {
        return codec_configuration_;
    }

    const std::string& media_foundation_h264_encoder::encoder_name() const noexcept
    {
        return encoder_name_;
    }

    bool media_foundation_h264_encoder::is_hardware() const noexcept
    {
        return diagnostics_.is_hardware;
    }

    const h264_encoder_diagnostics&
    media_foundation_h264_encoder::diagnostics() const noexcept
    {
        return diagnostics_;
    }

    void media_foundation_h264_encoder::close() noexcept
    {
        if (closed_)
        {
            return;
        }

        closed_ = true;
        av_packet_free(&packet_);
        av_frame_free(&frame_);
        avcodec_free_context(&codec_context_);
    }
}
