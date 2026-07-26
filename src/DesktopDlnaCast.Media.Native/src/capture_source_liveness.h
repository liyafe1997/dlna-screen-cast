#pragma once

#include "ddc_media.h"

#include <cstdint>

namespace ddc
{
    [[nodiscard]] constexpr bool capture_source_was_lost(
        const std::int32_t source_kind,
        const bool native_source_exists,
        const bool native_source_is_minimized) noexcept
    {
        (void)native_source_is_minimized;
        return source_kind == DDC_CAPTURE_SOURCE_WINDOW && !native_source_exists;
    }
}
