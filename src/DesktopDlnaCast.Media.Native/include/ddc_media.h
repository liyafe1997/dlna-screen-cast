#pragma once

#include <stdint.h>

#if defined(_WIN32) && defined(DDC_MEDIA_NATIVE_EXPORTS)
#define DDC_API __declspec(dllexport)
#elif defined(_WIN32)
#define DDC_API __declspec(dllimport)
#else
#define DDC_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define DDC_ABI_VERSION 4
#define DDC_PACKET_FLAG_RANDOM_ACCESS_POINT 0x1

typedef void* ddc_session_handle;

typedef enum ddc_result {
    DDC_OK = 0,
    DDC_TIMEOUT = 1,
    DDC_END_OF_STREAM = 2,
    DDC_E_INVALID_ARGUMENT = -1,
    DDC_E_INVALID_STATE = -2,
    DDC_E_PLATFORM = -3,
    DDC_E_MEDIA_PIPELINE = -4,
    DDC_E_BUFFER_TOO_SMALL = -5,
    DDC_E_CANCELED = -6,
    DDC_E_INTERNAL = -127
} ddc_result;

typedef enum ddc_capture_source_kind {
    DDC_CAPTURE_SOURCE_DISPLAY = 0,
    DDC_CAPTURE_SOURCE_WINDOW = 1
} ddc_capture_source_kind;

typedef enum ddc_video_processor_backend {
    DDC_VIDEO_PROCESSOR_UNKNOWN = 0,
    DDC_VIDEO_PROCESSOR_D3D11 = 1,
    DDC_VIDEO_PROCESSOR_LIBSWSCALE = 2
} ddc_video_processor_backend;

typedef struct ddc_stream_config {
    int32_t struct_size;
    int32_t abi_version;
    int32_t source_kind;
    uint64_t source_handle;
    int32_t include_cursor;
    int32_t width;
    int32_t height;
    int32_t frame_rate;
    int32_t video_bitrate;
    int32_t gop_frames;
    int32_t audio_bitrate;
    int32_t include_audio;
    int32_t stream_mode;
    int32_t mute_local_playback;
} ddc_stream_config;

typedef struct ddc_session_statistics {
    int32_t struct_size;
    int32_t abi_version;
    int64_t captured_video_frames;
    int64_t encoded_video_frames;
    int64_t dropped_video_frames;
    int64_t captured_audio_packets;
    int64_t encoded_audio_frames;
    int64_t audio_device_changes;
    int64_t queue_overflows;
    int64_t timestamp_corrections;
    int64_t encoder_wait_100ns;
    int64_t local_playback_mute_changes;
    int64_t local_playback_mute_restore_failures;
} ddc_session_statistics;

typedef struct ddc_encoder_diagnostics {
    int32_t struct_size;
    int32_t abi_version;
    int32_t is_hardware;
    int32_t accepted_width;
    int32_t accepted_height;
    int32_t frame_rate_numerator;
    int32_t frame_rate_denominator;
    int32_t accepted_video_bitrate;
    int32_t h264_profile;
    int32_t accepted_gop_frames;
    int32_t accepted_b_frame_count;
    int32_t video_processor_backend;
    int32_t audio_enabled;
    int32_t accepted_audio_bitrate;
    int32_t audio_sample_rate;
    int32_t audio_channels;
} ddc_encoder_diagnostics;

DDC_API int32_t ddc_session_create(
    const ddc_stream_config* config,
    ddc_session_handle* result);
DDC_API int32_t ddc_session_start(ddc_session_handle handle);
DDC_API int32_t ddc_session_read(
    ddc_session_handle handle,
    uint8_t* buffer,
    int32_t buffer_capacity,
    int32_t* bytes_written,
    int64_t* timestamp_100ns,
    int32_t* packet_flags,
    uint32_t timeout_ms);
DDC_API int32_t ddc_session_get_statistics(
    ddc_session_handle handle,
    ddc_session_statistics* statistics);
DDC_API int32_t ddc_session_get_encoder_diagnostics(
    ddc_session_handle handle,
    ddc_encoder_diagnostics* diagnostics,
    uint8_t* encoder_name_utf8,
    int32_t encoder_name_capacity,
    int32_t* encoder_name_bytes_written);
DDC_API int32_t ddc_session_stop(ddc_session_handle handle);
DDC_API int32_t ddc_session_get_last_error(
    ddc_session_handle handle,
    uint8_t* utf8_buffer,
    int32_t buffer_capacity,
    int32_t* bytes_written);
DDC_API void ddc_session_destroy(ddc_session_handle handle);

#ifdef __cplusplus
}
#endif
