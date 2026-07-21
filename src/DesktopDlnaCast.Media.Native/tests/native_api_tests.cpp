#include "ddc_media.h"
#include "test_assert.h"

#include <winrt/base.h>

#include <array>
#include <cstdint>
#include <string>

namespace
{
    [[nodiscard]] ddc_stream_config valid_config()
    {
        ddc_stream_config config{};
        config.struct_size = static_cast<std::int32_t>(sizeof(config));
        config.abi_version = DDC_ABI_VERSION;
        config.source_kind = DDC_CAPTURE_SOURCE_DISPLAY;
        config.source_handle = 1;
        config.include_cursor = 1;
        config.width = 1280;
        config.height = 720;
        config.frame_rate = 30;
        config.video_bitrate = 3'000'000;
        config.gop_frames = 30;
        config.audio_bitrate = 128'000;
        config.include_audio = 0;
        config.stream_mode = 1;
        config.mute_local_playback = 0;
        return config;
    }
}

void run_bounded_packet_queue_tests();
void run_d3d11_video_processor_tests();
void run_media_foundation_aac_encoder_tests();
void run_media_foundation_h264_encoder_tests();
void run_mpeg_ts_muxer_tests();
void run_software_video_processor_tests();

int main()
{
    winrt::init_apartment(winrt::apartment_type::multi_threaded);

    run_bounded_packet_queue_tests();
    run_d3d11_video_processor_tests();
    run_media_foundation_h264_encoder_tests();
    run_media_foundation_aac_encoder_tests();
    run_mpeg_ts_muxer_tests();
    run_software_video_processor_tests();
    static_assert(DDC_ABI_VERSION == 4);
    static_assert(sizeof(ddc_stream_config) == 64);
    static_assert(sizeof(ddc_session_statistics) == 96);
    static_assert(sizeof(ddc_encoder_diagnostics) == 64);

    DDC_TEST_CHECK(ddc_session_create(nullptr, nullptr) == DDC_E_INVALID_ARGUMENT);
    auto invalid = valid_config();
    invalid.width = 1279;
    ddc_session_handle handle{};
    DDC_TEST_CHECK(ddc_session_create(&invalid, &handle) == DDC_E_INVALID_ARGUMENT);
    DDC_TEST_CHECK(handle == nullptr);

    invalid = valid_config();
    invalid.mute_local_playback = 1;
    DDC_TEST_CHECK(ddc_session_create(&invalid, &handle) == DDC_E_INVALID_ARGUMENT);
    DDC_TEST_CHECK(handle == nullptr);

    auto config = valid_config();
    DDC_TEST_CHECK(ddc_session_create(&config, &handle) == DDC_OK);
    DDC_TEST_CHECK(handle != nullptr);

    ddc_session_statistics statistics{};
    statistics.struct_size = static_cast<std::int32_t>(sizeof(statistics));
    statistics.abi_version = DDC_ABI_VERSION;
    DDC_TEST_CHECK(ddc_session_get_statistics(handle, &statistics) == DDC_OK);
    DDC_TEST_CHECK(statistics.captured_video_frames == 0);

    // Diagnostics and reads are unavailable until a real capture source starts.
    // A fabricated HWND/HMONITOR must not be passed into WGC merely to force a
    // component-test failure; the runtime probe covers Start and asynchronous cleanup.
    ddc_encoder_diagnostics diagnostics{};
    diagnostics.struct_size = static_cast<std::int32_t>(sizeof(diagnostics));
    diagnostics.abi_version = DDC_ABI_VERSION;
    std::array<std::uint8_t, 256> encoder_name{};
    std::int32_t encoder_name_bytes{};
    DDC_TEST_CHECK(ddc_session_get_encoder_diagnostics(
               handle,
               &diagnostics,
               encoder_name.data(),
               static_cast<std::int32_t>(encoder_name.size()),
               &encoder_name_bytes) == DDC_E_INVALID_STATE);
    std::array<std::uint8_t, 4096> error{};
    std::int32_t error_bytes{};
    DDC_TEST_CHECK(ddc_session_get_last_error(
               handle,
               error.data(),
               static_cast<std::int32_t>(error.size()),
               &error_bytes) == DDC_OK);
    const std::string error_text(
        reinterpret_cast<const char*>(error.data()),
        static_cast<std::size_t>(error_bytes));
    DDC_TEST_CHECK(!error_text.empty());

    std::array<std::uint8_t, 188> packet{};
    std::int32_t bytes_written{};
    std::int64_t timestamp{};
    std::int32_t flags{};
    DDC_TEST_CHECK(ddc_session_read(
               handle,
               packet.data(),
               static_cast<std::int32_t>(packet.size()),
               &bytes_written,
               &timestamp,
               &flags,
               10) == DDC_E_INVALID_STATE);

    DDC_TEST_CHECK(ddc_session_stop(handle) == DDC_OK);
    DDC_TEST_CHECK(ddc_session_stop(handle) == DDC_OK);
    DDC_TEST_CHECK(ddc_session_read(
               handle,
               packet.data(),
               static_cast<std::int32_t>(packet.size()),
               &bytes_written,
               &timestamp,
               &flags,
               10) == DDC_END_OF_STREAM);
    ddc_session_destroy(handle);
    ddc_session_destroy(nullptr);


    // Exercise deterministic pre-start cleanup repeatedly without passing an
    // intentionally invalid OS handle into Windows.Graphics.Capture.
    for (std::int32_t iteration = 0; iteration < 20; ++iteration)
    {
        handle = nullptr;
        config = valid_config();
        DDC_TEST_CHECK(ddc_session_create(&config, &handle) == DDC_OK);
        DDC_TEST_CHECK(ddc_session_stop(handle) == DDC_OK);
        DDC_TEST_CHECK(ddc_session_stop(handle) == DDC_OK);
        ddc_session_destroy(handle);
    }

    winrt::uninit_apartment();
    return 0;
}
