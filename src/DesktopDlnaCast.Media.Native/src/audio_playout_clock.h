#pragma once

#include <algorithm>
#include <cstdint>

namespace ddc
{
    class audio_playout_clock final
    {
    public:
        static constexpr std::int64_t jitter_buffer_100ns = 1'000'000;

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
