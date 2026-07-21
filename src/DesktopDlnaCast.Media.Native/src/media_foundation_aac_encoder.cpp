#include "media_foundation_aac_encoder.h"

extern "C"
{
#include <libavutil/channel_layout.h>
#include <libavutil/error.h>
#include <libavutil/samplefmt.h>
}

#include <algorithm>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <utility>

namespace
{
    constexpr AVRational audio_time_base{ 1, ddc::media_foundation_aac_encoder::sample_rate };
    constexpr AVRational media_foundation_time_base{ 1, 10'000'000 };

    [[noreturn]] void throw_ffmpeg_error(const int result, const char* operation)
    {
        char buffer[AV_ERROR_MAX_STRING_SIZE]{};
        av_strerror(result, buffer, sizeof(buffer));
        throw std::runtime_error(
            std::string("FFmpeg aac_mf failed while ") + operation + ": " + buffer + ".");
    }
}

namespace ddc
{
    media_foundation_aac_encoder::media_foundation_aac_encoder(
        const std::int32_t bitrate,
        aac_sample_callback callback)
        : callback_(std::move(callback))
    {
        if (bitrate < 32'000 || bitrate > 512'000 || !callback_)
        {
            throw std::invalid_argument("The AAC encoder configuration is invalid.");
        }

        codec_ = avcodec_find_encoder_by_name("aac_mf");
        if (codec_ == nullptr)
        {
            throw std::runtime_error(
                "The pinned FFmpeg build does not contain the Media Foundation aac_mf encoder.");
        }

        codec_context_ = avcodec_alloc_context3(codec_);
        if (codec_context_ == nullptr)
        {
            throw std::bad_alloc();
        }

        codec_context_->codec_type = AVMEDIA_TYPE_AUDIO;
        codec_context_->codec_id = AV_CODEC_ID_AAC;
        codec_context_->sample_fmt = AV_SAMPLE_FMT_S16;
        codec_context_->sample_rate = sample_rate;
        codec_context_->time_base = audio_time_base;
        codec_context_->bit_rate = bitrate;
        codec_context_->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;
        av_channel_layout_default(&codec_context_->ch_layout, channels);
        const int open_result = avcodec_open2(codec_context_, codec_, nullptr);
        if (open_result < 0)
        {
            close();
            throw_ffmpeg_error(open_result, "opening the Microsoft AAC-LC encoder");
        }

        frame_ = av_frame_alloc();
        packet_ = av_packet_alloc();
        if (frame_ == nullptr || packet_ == nullptr)
        {
            close();
            throw std::bad_alloc();
        }

        frame_->format = AV_SAMPLE_FMT_S16;
        frame_->sample_rate = sample_rate;
        frame_->nb_samples = samples_per_frame;
        av_channel_layout_copy(&frame_->ch_layout, &codec_context_->ch_layout);
        const int buffer_result = av_frame_get_buffer(frame_, 0);
        if (buffer_result < 0)
        {
            close();
            throw_ffmpeg_error(buffer_result, "allocating a PCM input frame");
        }

        if (codec_context_->extradata != nullptr && codec_context_->extradata_size > 0)
        {
            codec_configuration_.assign(
                codec_context_->extradata,
                codec_context_->extradata + codec_context_->extradata_size);
        }

        encoder_name_ = "Microsoft Media Foundation AAC-LC encoder (FFmpeg aac_mf)";
    }

    media_foundation_aac_encoder::~media_foundation_aac_encoder()
    {
        close();
    }

    void media_foundation_aac_encoder::encode(
        const std::span<const std::int16_t> interleaved_pcm,
        const std::int64_t start_sample)
    {
        constexpr std::size_t sample_count =
            static_cast<std::size_t>(samples_per_frame) * channels;
        if (closed_ || draining_ || codec_context_ == nullptr || frame_ == nullptr ||
            packet_ == nullptr)
        {
            throw std::logic_error("The AAC encoder is not accepting input.");
        }

        if (interleaved_pcm.size() != sample_count || start_sample < 0)
        {
            throw std::invalid_argument("A complete stereo PCM frame and timestamp are required.");
        }

        const int writable_result = av_frame_make_writable(frame_);
        if (writable_result < 0)
        {
            throw_ffmpeg_error(writable_result, "making the PCM input frame writable");
        }

        std::memcpy(
            frame_->data[0],
            interleaved_pcm.data(),
            interleaved_pcm.size_bytes());
        frame_->pts = start_sample;
        frame_->duration = samples_per_frame;
        int send_result = avcodec_send_frame(codec_context_, frame_);
        if (send_result == AVERROR(EAGAIN))
        {
            receive_available_packets(false);
            send_result = avcodec_send_frame(codec_context_, frame_);
        }

        if (send_result < 0)
        {
            throw_ffmpeg_error(send_result, "submitting a PCM frame");
        }

        receive_available_packets(false);
    }

    void media_foundation_aac_encoder::receive_available_packets(const bool draining)
    {
        while (true)
        {
            const int result = avcodec_receive_packet(codec_context_, packet_);
            if (result == AVERROR(EAGAIN) || result == AVERROR_EOF)
            {
                if (draining && result == AVERROR(EAGAIN))
                {
                    throw std::runtime_error(
                        "FFmpeg aac_mf requested more input while the encoder was draining.");
                }

                return;
            }

            if (result < 0)
            {
                throw_ffmpeg_error(result, "receiving encoded AAC output");
            }

            try
            {
                aac_encoded_sample sample;
                sample.bytes.assign(packet_->data, packet_->data + packet_->size);
                const std::int64_t pts = packet_->pts == AV_NOPTS_VALUE
                    ? packet_->dts
                    : packet_->pts;
                sample.timestamp_100ns = pts == AV_NOPTS_VALUE
                    ? 0
                    : av_rescale_q(pts, audio_time_base, media_foundation_time_base);
                const std::int64_t duration = packet_->duration > 0
                    ? packet_->duration
                    : samples_per_frame;
                sample.duration_100ns = av_rescale_q(
                    duration,
                    audio_time_base,
                    media_foundation_time_base);
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

    void media_foundation_aac_encoder::drain()
    {
        if (closed_ || draining_ || codec_context_ == nullptr)
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

    const std::vector<std::uint8_t>&
    media_foundation_aac_encoder::codec_configuration() const noexcept
    {
        return codec_configuration_;
    }

    const std::string& media_foundation_aac_encoder::encoder_name() const noexcept
    {
        return encoder_name_;
    }

    std::int32_t media_foundation_aac_encoder::accepted_bitrate() const noexcept
    {
        return static_cast<std::int32_t>(std::clamp<std::int64_t>(
            codec_context_ == nullptr ? 0 : codec_context_->bit_rate,
            0,
            std::numeric_limits<std::int32_t>::max()));
    }

    void media_foundation_aac_encoder::close() noexcept
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
