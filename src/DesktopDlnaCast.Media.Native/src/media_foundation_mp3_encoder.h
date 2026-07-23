#pragma once

extern "C"
{
#include <libavcodec/avcodec.h>
#include <libavutil/frame.h>
}

#include "bounded_packet_queue.h"
#include "ddc_media.h"

#include <cstdint>
#include <functional>
#include <span>
#include <string>
#include <vector>

namespace ddc
{
    using mp3_packet_callback = std::function<void(media_packet packet)>;

    class media_foundation_mp3_encoder final
    {
    public:
        static constexpr std::int32_t sample_rate = 48'000;
        static constexpr std::int32_t channels = 2;

        media_foundation_mp3_encoder(std::int32_t bitrate, mp3_packet_callback callback);
        media_foundation_mp3_encoder(const media_foundation_mp3_encoder&) = delete;
        media_foundation_mp3_encoder& operator=(const media_foundation_mp3_encoder&) = delete;
        ~media_foundation_mp3_encoder();

        void encode(std::span<const std::int16_t> interleaved_pcm, std::int64_t start_sample);
        void drain();

        [[nodiscard]] const std::string& encoder_name() const noexcept;
        [[nodiscard]] std::int32_t accepted_bitrate() const noexcept;

    private:
        void encode_frame(std::span<const std::int16_t> samples, std::int64_t start_sample);
        void receive_available_packets(bool draining);
        void close() noexcept;

        mp3_packet_callback callback_;
        const AVCodec* codec_{};
        AVCodecContext* codec_context_{};
        AVFrame* frame_{};
        AVPacket* packet_{};
        std::vector<std::int16_t> pending_;
        std::string encoder_name_;
        std::int64_t pending_start_sample_{};
        std::int32_t frame_samples_per_channel_{ 1152 };
        bool draining_{};
        bool closed_{};
    };
}
