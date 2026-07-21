#pragma once

extern "C"
{
#include <libavcodec/avcodec.h>
#include <libavutil/frame.h>
}

#include <cstdint>
#include <functional>
#include <span>
#include <string>
#include <vector>

namespace ddc
{
    struct aac_encoded_sample final
    {
        std::vector<std::uint8_t> bytes;
        std::int64_t timestamp_100ns{};
        std::int64_t duration_100ns{};
    };

    using aac_sample_callback = std::function<void(aac_encoded_sample sample)>;

    class media_foundation_aac_encoder final
    {
    public:
        static constexpr std::int32_t sample_rate = 48'000;
        static constexpr std::int32_t channels = 2;
        static constexpr std::int32_t samples_per_frame = 1'024;

        media_foundation_aac_encoder(std::int32_t bitrate, aac_sample_callback callback);
        media_foundation_aac_encoder(const media_foundation_aac_encoder&) = delete;
        media_foundation_aac_encoder& operator=(const media_foundation_aac_encoder&) = delete;
        ~media_foundation_aac_encoder();

        void encode(std::span<const std::int16_t> interleaved_pcm, std::int64_t start_sample);
        void drain();

        [[nodiscard]] const std::vector<std::uint8_t>& codec_configuration() const noexcept;
        [[nodiscard]] const std::string& encoder_name() const noexcept;
        [[nodiscard]] std::int32_t accepted_bitrate() const noexcept;

    private:
        void receive_available_packets(bool draining);
        void close() noexcept;

        aac_sample_callback callback_;
        const AVCodec* codec_{};
        AVCodecContext* codec_context_{};
        AVFrame* frame_{};
        AVPacket* packet_{};
        std::vector<std::uint8_t> codec_configuration_;
        std::string encoder_name_;
        bool draining_{};
        bool closed_{};
    };
}
