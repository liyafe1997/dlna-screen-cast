#pragma once

#include <cstdint>
#include <span>
#include <vector>

namespace ddc
{
    struct normalized_h264_access_unit final
    {
        std::vector<std::uint8_t> bytes;
        bool is_random_access_point{};
    };

    class h264_access_unit_normalizer final
    {
    public:
        [[nodiscard]] normalized_h264_access_unit normalize(
            std::span<const std::uint8_t> bytes);

    private:
        std::vector<std::uint8_t> sequence_parameter_set_;
        std::vector<std::uint8_t> picture_parameter_set_;
    };
}
