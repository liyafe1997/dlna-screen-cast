#pragma once

#include "ddc_media.h"
#include "bounded_packet_queue.h"
#include "d3d11_video_processor.h"
#include "graphics_capture_source.h"
#include "media_foundation_aac_encoder.h"
#include "media_foundation_h264_encoder.h"
#include "mpeg_ts_muxer.h"
#include "software_video_processor.h"
#include "wasapi_loopback_capture.h"

#include <memory>
#include <cstdint>
#include <mutex>
#include <optional>
#include <string>

namespace ddc
{
    class media_session final
    {
    public:
        explicit media_session(const ddc_stream_config& config);
        media_session(const media_session&) = delete;
        media_session& operator=(const media_session&) = delete;
        ~media_session();

        [[nodiscard]] std::int32_t start();
        [[nodiscard]] std::int32_t read(
            std::uint8_t* buffer,
            std::int32_t buffer_capacity,
            std::int32_t* bytes_written,
            std::int64_t* timestamp_100ns,
            std::int32_t* packet_flags,
            std::uint32_t timeout_ms);
        [[nodiscard]] std::int32_t get_statistics(ddc_session_statistics* statistics);
        [[nodiscard]] std::int32_t copy_encoder_diagnostics(
            ddc_encoder_diagnostics* diagnostics,
            std::uint8_t* encoder_name,
            std::int32_t encoder_name_capacity,
            std::int32_t* encoder_name_bytes_written);
        [[nodiscard]] std::int32_t stop();
        [[nodiscard]] std::int32_t copy_last_error(
            std::uint8_t* buffer,
            std::int32_t buffer_capacity,
            std::int32_t* bytes_written);

    private:
        enum class state
        {
            created,
            starting,
            running,
            failed,
            stopping,
            stopped,
        };

        enum class video_processor_backend
        {
            undecided,
            d3d11,
            software,
        };

        void process_captured_frame(
            const winrt::com_ptr<ID3D11Texture2D>& texture,
            std::int32_t width,
            std::int32_t height,
            std::int64_t timestamp_100ns,
            bool is_repeated_frame);
        void create_encoder_and_muxer();
        void record_pipeline_failure(std::exception_ptr failure) noexcept;
        void cleanup_pipeline() noexcept;
        void set_error_locked(std::string message);

        std::mutex operation_mutex_;
        std::mutex read_mutex_;
        std::mutex mutex_;
        ddc_stream_config config_{};
        ddc_session_statistics statistics_{};
        std::optional<ddc_encoder_diagnostics> encoder_diagnostics_;
        std::string encoder_name_;
        std::string video_processor_fallback_reason_;
        std::string last_error_;
        state state_{ state::created };
        bounded_packet_queue output_packets_{ 64, 8U * 1024U * 1024U };
        std::optional<media_packet> pending_packet_;
        std::unique_ptr<graphics_capture_source> capture_;
        std::unique_ptr<d3d11_video_processor> video_processor_;
        std::unique_ptr<software_video_processor> software_video_processor_;
        std::unique_ptr<media_foundation_h264_encoder> video_encoder_;
        std::unique_ptr<media_foundation_aac_encoder> audio_encoder_;
        std::unique_ptr<wasapi_loopback_capture> audio_capture_;
        std::unique_ptr<mpeg_ts_muxer> muxer_;
        std::int64_t session_start_timestamp_100ns_{};
        std::int64_t next_output_timestamp_100ns_{};
        video_processor_backend video_processor_backend_{ video_processor_backend::undecided };
    };

    [[nodiscard]] std::int32_t validate_config(const ddc_stream_config* config) noexcept;
}
