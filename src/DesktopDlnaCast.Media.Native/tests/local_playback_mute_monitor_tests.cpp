#include "local_playback_mute_monitor.h"
#include "test_assert.h"

#include <array>
#include <cstdint>
#include <vector>

void run_local_playback_mute_monitor_tests()
{
    std::array<std::int16_t, 8> silence{};
    std::array<std::int16_t, 8> signal{ 0, 0, 64, 0, 0, 0, 0, 0 };

    ddc::local_playback_mute_monitor compatible;
    DDC_TEST_CHECK(!compatible.should_apply_mute(silence));
    DDC_TEST_CHECK(compatible.should_apply_mute(signal));
    DDC_TEST_CHECK(!compatible.should_apply_mute(signal));
    DDC_TEST_CHECK(!compatible.observe_after_mute(signal, 1'024));
    DDC_TEST_CHECK(!compatible.fallback_required());

    ddc::local_playback_mute_monitor incompatible;
    DDC_TEST_CHECK(incompatible.should_apply_mute(signal));
    for (std::int64_t observed = 0;
         observed < ddc::local_playback_mute_monitor::verification_frames;
         observed += 1'024)
    {
        const std::int64_t remaining =
            ddc::local_playback_mute_monitor::verification_frames - observed;
        const std::int64_t frames = remaining < 1'024 ? remaining : 1'024;
        const bool fallback = incompatible.observe_after_mute(silence, frames);
        DDC_TEST_CHECK(fallback ==
            (observed + frames >= ddc::local_playback_mute_monitor::verification_frames));
    }
    DDC_TEST_CHECK(incompatible.fallback_required());
    DDC_TEST_CHECK(!incompatible.observe_after_mute(silence, 1'024));

    ddc::local_playback_mute_monitor low_level_noise;
    std::vector<std::int16_t> noise(2'048, 16);
    DDC_TEST_CHECK(!low_level_noise.should_apply_mute(noise));
}
