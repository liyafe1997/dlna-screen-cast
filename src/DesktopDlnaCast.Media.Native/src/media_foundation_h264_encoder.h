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
    struct h264_encoded_sample final
    {
        std::vector<std::uint8_t> bytes;
        std::int64_t timestamp_100ns{};
        std::int64_t duration_100ns{};
        bool key_frame{};
    };

    using h264_sample_callback = std::function<void(h264_encoded_sample sample)>;

    struct h264_encoder_diagnostics final
    {
        std::int32_t accepted_width{};
        std::int32_t accepted_height{};
        std::int32_t frame_rate_numerator{};
        std::int32_t frame_rate_denominator{};
        std::int32_t accepted_video_bitrate{};
        std::int32_t h264_profile{};
        std::int32_t accepted_gop_frames{ -1 };
        std::int32_t accepted_b_frame_count{ -1 };
        bool is_hardware{};
    };

    enum class h264_encoder_preference
    {
        hardware_preferred,
        software_only,
    };

    class media_foundation_h264_encoder final
    {
    public:
        media_foundation_h264_encoder(
            std::int32_t width,
            std::int32_t height,
            std::int32_t frame_rate,
            std::int32_t bitrate,
            std::int32_t gop_frames,
            h264_sample_callback callback,
            h264_encoder_preference preference = h264_encoder_preference::hardware_preferred);
        media_foundation_h264_encoder(const media_foundation_h264_encoder&) = delete;
        media_foundation_h264_encoder& operator=(const media_foundation_h264_encoder&) = delete;
        ~media_foundation_h264_encoder();

        void encode(std::span<const std::uint8_t> nv12_frame, std::int64_t timestamp_100ns);
        void drain();

        [[nodiscard]] const std::vector<std::uint8_t>& codec_configuration() const noexcept;
        [[nodiscard]] const std::string& encoder_name() const noexcept;
        [[nodiscard]] bool is_hardware() const noexcept;
        [[nodiscard]] const h264_encoder_diagnostics& diagnostics() const noexcept;

    private:
        [[nodiscard]] bool try_open_encoder(bool hardware);
        void configure_context(AVCodecContext& context, bool hardware);
        void receive_available_packets(bool draining);
        void update_codec_configuration();
        void close() noexcept;

        const std::int32_t width_;
        const std::int32_t height_;
        const std::int32_t frame_rate_;
        const std::int32_t bitrate_;
        const std::int32_t gop_frames_;
        const std::int64_t frame_duration_100ns_;
        h264_sample_callback callback_;
        const AVCodec* codec_{};
        AVCodecContext* codec_context_{};
        AVFrame* frame_{};
        AVPacket* packet_{};
        std::vector<std::uint8_t> codec_configuration_;
        h264_encoder_diagnostics diagnostics_{};
        std::string encoder_name_;
        std::string hardware_fallback_reason_;
        std::int64_t last_requested_keyframe_timestamp_100ns_{ -1 };
        bool draining_{};
        bool closed_{};
    };
}
