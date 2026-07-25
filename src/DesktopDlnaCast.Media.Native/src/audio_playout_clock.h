#pragma once

#include <algorithm>
#include <cstdint>

namespace ddc
{
    class audio_playout_clock final
    {
    public:
        static constexpr std::int64_t jitter_buffer_100ns = 1'000'000;

        [[nodiscard]] static constexpr std::int64_t reconcile_delta_frames(
            const std::int64_t packet_start,
            const std::int64_t pending_end,
            const std::int32_t sample_rate) noexcept
        {
            if (sample_rate <= 0)
            {
                return 0;
            }

            const std::int64_t delta = packet_start - pending_end;
            const std::int64_t tolerance = std::max<std::int64_t>(
                1,
                sample_rate / 500);
            return delta >= -tolerance && delta <= tolerance ? 0 : delta;
        }

        [[nodiscard]] static constexpr std::int64_t target_frames(
            const std::int64_t elapsed_100ns,
            const std::int32_t sample_rate) noexcept
        {
            if (elapsed_100ns <= jitter_buffer_100ns || sample_rate <= 0)
            {
                return 0;
            }

            return (elapsed_100ns - jitter_buffer_100ns) * sample_rate /
                10'000'000LL;
        }
    };
}
