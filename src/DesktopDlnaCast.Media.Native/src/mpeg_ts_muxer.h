#pragma once

#include "bounded_packet_queue.h"
#include "ddc_media.h"

extern "C"
{
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
}

#include <cstdint>
#include <exception>
#include <functional>
#include <mutex>
#include <span>
#include <vector>

namespace ddc
{
    struct encoded_video_sample final
    {
        std::span<const std::uint8_t> bytes;
        std::int64_t timestamp_100ns{};
        std::int64_t duration_100ns{};
        bool key_frame{};
    };

    struct encoded_audio_sample final
    {
        std::span<const std::uint8_t> bytes;
        std::int64_t timestamp_100ns{};
        std::int64_t duration_100ns{};
    };

    using muxed_packet_callback = std::function<void(media_packet packet)>;

    class mpeg_ts_muxer final
    {
    public:
        mpeg_ts_muxer(
            std::int32_t width,
            std::int32_t height,
            std::int32_t bitrate,
            std::span<const std::uint8_t> codec_configuration,
            std::int32_t audio_bitrate,
            std::span<const std::uint8_t> audio_codec_configuration,
            muxed_packet_callback callback);
        mpeg_ts_muxer(const mpeg_ts_muxer&) = delete;
        mpeg_ts_muxer& operator=(const mpeg_ts_muxer&) = delete;
        ~mpeg_ts_muxer();

        void write_video(const encoded_video_sample& sample);
        void write_audio(const encoded_audio_sample& sample);
        void finish();

    private:
        static int write_packet(void* opaque, const std::uint8_t* buffer, int buffer_size) noexcept;
        int append_output(const std::uint8_t* buffer, int buffer_size) noexcept;
        void throw_if_callback_failed();
        void emit_current(bool random_access_point, std::int64_t timestamp_100ns);
        void close() noexcept;

        std::mutex mutex_;
        muxed_packet_callback callback_;
        AVFormatContext* format_context_{};
        AVIOContext* io_context_{};
        AVStream* video_stream_{};
        AVStream* audio_stream_{};
        std::vector<std::uint8_t> header_bytes_;
        std::vector<std::uint8_t> current_bytes_;
        std::exception_ptr callback_failure_;
        bool finished_{};
    };
}
