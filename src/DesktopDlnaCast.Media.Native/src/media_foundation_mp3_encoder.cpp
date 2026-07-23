#include "media_foundation_mp3_encoder.h"

extern "C"
{
#include <libavutil/channel_layout.h>
#include <libavutil/error.h>
#include <libavutil/samplefmt.h>
}

#include <algorithm>
#include <cstring>
#include <stdexcept>
#include <utility>

namespace
{
    constexpr AVRational audio_time_base{
        1,
        ddc::media_foundation_mp3_encoder::sample_rate,
    };
    constexpr AVRational media_time_base{ 1, 10'000'000 };

    [[noreturn]] void throw_ffmpeg_error(const int result, const char* operation)
    {
        char buffer[AV_ERROR_MAX_STRING_SIZE]{};
        av_strerror(result, buffer, sizeof(buffer));
        throw std::runtime_error(
            std::string("FFmpeg mp3_mf failed while ") + operation + ": " + buffer + ".");
    }
}

namespace ddc
{
    media_foundation_mp3_encoder::media_foundation_mp3_encoder(
        const std::int32_t bitrate,
        mp3_packet_callback callback)
        : callback_(std::move(callback))
    {
        if (bitrate < 32'000 || bitrate > 320'000 || !callback_)
        {
            throw std::invalid_argument("The MP3 encoder configuration is invalid.");
        }

        codec_ = avcodec_find_encoder_by_name("mp3_mf");
        if (codec_ == nullptr)
        {
            throw std::runtime_error(
                "The pinned FFmpeg build does not contain the Media Foundation mp3_mf encoder.");
        }

        codec_context_ = avcodec_alloc_context3(codec_);
        if (codec_context_ == nullptr)
        {
            throw std::bad_alloc();
        }

        codec_context_->codec_type = AVMEDIA_TYPE_AUDIO;
        codec_context_->codec_id = AV_CODEC_ID_MP3;
        codec_context_->sample_fmt = AV_SAMPLE_FMT_S16;
        codec_context_->sample_rate = sample_rate;
        codec_context_->time_base = audio_time_base;
        codec_context_->bit_rate = bitrate;
        av_channel_layout_default(&codec_context_->ch_layout, channels);
        const int open_result = avcodec_open2(codec_context_, codec_, nullptr);
        if (open_result < 0)
        {
            close();
            throw_ffmpeg_error(open_result, "opening the Microsoft MP3 encoder");
        }

        if (codec_context_->frame_size > 0)
        {
            frame_samples_per_channel_ = codec_context_->frame_size;
        }

        if (frame_samples_per_channel_ <= 0 || frame_samples_per_channel_ > 4096)
        {
            close();
            throw std::runtime_error("The MP3 encoder returned an invalid frame size.");
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
        frame_->nb_samples = frame_samples_per_channel_;
        av_channel_layout_copy(&frame_->ch_layout, &codec_context_->ch_layout);
        const int buffer_result = av_frame_get_buffer(frame_, 0);
        if (buffer_result < 0)
        {
            close();
            throw_ffmpeg_error(buffer_result, "allocating a PCM input frame");
        }

        pending_.reserve(
            static_cast<std::size_t>(frame_samples_per_channel_ + 1024) * channels);
        encoder_name_ = "Microsoft Media Foundation MP3 encoder (FFmpeg mp3_mf)";
    }

    media_foundation_mp3_encoder::~media_foundation_mp3_encoder()
    {
        close();
    }

    void media_foundation_mp3_encoder::encode(
        const std::span<const std::int16_t> interleaved_pcm,
        const std::int64_t start_sample)
    {
        if (closed_ || draining_ || codec_context_ == nullptr || start_sample < 0 ||
            interleaved_pcm.empty() || interleaved_pcm.size() % channels != 0)
        {
            throw std::invalid_argument("A complete stereo PCM block and timestamp are required.");
        }

        if (pending_.empty())
        {
            pending_start_sample_ = start_sample;
        }

        pending_.insert(pending_.end(), interleaved_pcm.begin(), interleaved_pcm.end());
        const std::size_t frame_samples =
            static_cast<std::size_t>(frame_samples_per_channel_) * channels;
        while (pending_.size() >= frame_samples)
        {
            encode_frame(
                std::span<const std::int16_t>(pending_.data(), frame_samples),
                pending_start_sample_);
            pending_.erase(
                pending_.begin(),
                pending_.begin() + static_cast<std::ptrdiff_t>(frame_samples));
            pending_start_sample_ += frame_samples_per_channel_;
        }
    }

    void media_foundation_mp3_encoder::encode_frame(
        const std::span<const std::int16_t> samples,
        const std::int64_t start_sample)
    {
        const int writable_result = av_frame_make_writable(frame_);
        if (writable_result < 0)
        {
            throw_ffmpeg_error(writable_result, "making the PCM input frame writable");
        }

        std::memcpy(frame_->data[0], samples.data(), samples.size_bytes());
        frame_->pts = start_sample;
        const int send_result = avcodec_send_frame(codec_context_, frame_);
        if (send_result < 0)
        {
            throw_ffmpeg_error(send_result, "submitting a PCM frame");
        }

        receive_available_packets(false);
    }

    void media_foundation_mp3_encoder::receive_available_packets(const bool draining)
    {
        while (true)
        {
            const int result = avcodec_receive_packet(codec_context_, packet_);
            if (result == AVERROR(EAGAIN) || result == AVERROR_EOF)
            {
                return;
            }

            if (result < 0)
            {
                throw_ffmpeg_error(result, "receiving encoded MP3 output");
            }

            const std::int64_t timestamp = av_rescale_q(
                packet_->pts,
                audio_time_base,
                media_time_base);
            std::vector<std::uint8_t> bytes(
                packet_->data,
                packet_->data + packet_->size);
            av_packet_unref(packet_);
            callback_({
                std::move(bytes),
                std::max<std::int64_t>(timestamp, 0),
                DDC_PACKET_FLAG_RANDOM_ACCESS_POINT,
            });
            static_cast<void>(draining);
        }
    }

    void media_foundation_mp3_encoder::drain()
    {
        if (closed_ || draining_ || codec_context_ == nullptr)
        {
            return;
        }

        draining_ = true;
        if (!pending_.empty())
        {
            const std::size_t frame_samples =
                static_cast<std::size_t>(frame_samples_per_channel_) * channels;
            pending_.resize(frame_samples, 0);
            encode_frame(
                std::span<const std::int16_t>(pending_.data(), frame_samples),
                pending_start_sample_);
            pending_.clear();
        }

        const int result = avcodec_send_frame(codec_context_, nullptr);
        if (result < 0 && result != AVERROR_EOF)
        {
            throw_ffmpeg_error(result, "starting encoder drain");
        }

        receive_available_packets(true);
    }

    const std::string& media_foundation_mp3_encoder::encoder_name() const noexcept
    {
        return encoder_name_;
    }

    std::int32_t media_foundation_mp3_encoder::accepted_bitrate() const noexcept
    {
        return codec_context_ == nullptr
            ? 0
            : static_cast<std::int32_t>(codec_context_->bit_rate);
    }

    void media_foundation_mp3_encoder::close() noexcept
    {
        closed_ = true;
        av_packet_free(&packet_);
        av_frame_free(&frame_);
        avcodec_free_context(&codec_context_);
        codec_ = nullptr;
    }
}
