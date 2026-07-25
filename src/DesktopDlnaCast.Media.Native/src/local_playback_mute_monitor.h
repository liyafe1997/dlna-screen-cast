#pragma once

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <span>

namespace ddc
{
    class local_playback_mute_monitor final
    {
    public:
        static constexpr std::int16_t audible_peak_threshold = 32;
        static constexpr std::int64_t verification_frames = 24'000;

        [[nodiscard]] bool should_apply_mute(
            const std::span<const std::int16_t> samples) noexcept
        {
            if (mute_applied_ || fallback_required_ || !contains_audible_signal(samples))
            {
                return false;
            }

            mute_applied_ = true;
            return true;
        }

        [[nodiscard]] bool observe_after_mute(
            const std::span<const std::int16_t> samples,
            const std::int64_t frames) noexcept
        {
            if (!mute_applied_ || fallback_required_ || signal_preserved_ || frames <= 0)
            {
                return false;
            }

            if (contains_audible_signal(samples))
            {
                signal_preserved_ = true;
                return false;
            }

            verified_frames_ += frames;
            if (verified_frames_ < verification_frames)
            {
                return false;
            }

            fallback_required_ = true;
            return true;
        }

        [[nodiscard]] bool fallback_required() const noexcept
        {
            return fallback_required_;
        }

    private:
        [[nodiscard]] static bool contains_audible_signal(
            const std::span<const std::int16_t> samples) noexcept
        {
            return std::ranges::any_of(samples, [](const std::int16_t sample)
            {
                const std::int32_t widened = sample;
                const std::int32_t magnitude = widened < 0 ? -widened : widened;
                return magnitude > audible_peak_threshold;
            });
        }

        std::int64_t verified_frames_{};
        bool mute_applied_{};
        bool signal_preserved_{};
        bool fallback_required_{};
    };
}
