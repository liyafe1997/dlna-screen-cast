#include "audio_playout_clock.h"
#include "test_assert.h"

void run_audio_playout_clock_tests()
{
    constexpr std::int32_t sample_rate = 48'000;

    DDC_TEST_CHECK(ddc::audio_playout_clock::target_frames(-1, sample_rate) == 0);
    DDC_TEST_CHECK(ddc::audio_playout_clock::target_frames(0, sample_rate) == 0);
    DDC_TEST_CHECK(ddc::audio_playout_clock::target_frames(
        ddc::audio_playout_clock::jitter_buffer_100ns,
        sample_rate) == 0);
    DDC_TEST_CHECK(ddc::audio_playout_clock::target_frames(
        ddc::audio_playout_clock::jitter_buffer_100ns + 10'000,
        sample_rate) == 48);
    DDC_TEST_CHECK(ddc::audio_playout_clock::target_frames(
        ddc::audio_playout_clock::jitter_buffer_100ns + 10'000'000,
        sample_rate) == sample_rate);
    DDC_TEST_CHECK(ddc::audio_playout_clock::target_frames(
        ddc::audio_playout_clock::jitter_buffer_100ns + 10'000'000,
        0) == 0);

    DDC_TEST_CHECK(ddc::audio_playout_clock::reconcile_delta_frames(
        10'000,
        10'000,
        sample_rate) == 0);
    DDC_TEST_CHECK(ddc::audio_playout_clock::reconcile_delta_frames(
        10'096,
        10'000,
        sample_rate) == 0);
    DDC_TEST_CHECK(ddc::audio_playout_clock::reconcile_delta_frames(
        9'904,
        10'000,
        sample_rate) == 0);
    DDC_TEST_CHECK(ddc::audio_playout_clock::reconcile_delta_frames(
        10'097,
        10'000,
        sample_rate) == 97);
    DDC_TEST_CHECK(ddc::audio_playout_clock::reconcile_delta_frames(
        9'903,
        10'000,
        sample_rate) == -97);
    DDC_TEST_CHECK(ddc::audio_playout_clock::reconcile_delta_frames(
        10'100,
        10'000,
        0) == 0);
}
