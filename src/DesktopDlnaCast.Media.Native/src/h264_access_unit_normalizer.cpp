#include "h264_access_unit_normalizer.h"

#include <cstddef>

namespace
{
    struct annex_b_nal final
    {
        std::size_t start{};
        std::size_t header{};
        std::size_t end{};
    };

    [[nodiscard]] std::vector<annex_b_nal> find_annex_b_nals(
        const std::span<const std::uint8_t> bytes)
    {
        struct start_code final
        {
            std::size_t start{};
            std::size_t header{};
        };

        std::vector<start_code> starts;
        for (std::size_t index = 0; index + 3U <= bytes.size();)
        {
            if (bytes[index] != 0 || bytes[index + 1U] != 0)
            {
                ++index;
                continue;
            }

            if (bytes[index + 2U] == 1)
            {
                starts.push_back({ index, index + 3U });
                index += 3U;
                continue;
            }

            if (index + 4U <= bytes.size() && bytes[index + 2U] == 0 &&
                bytes[index + 3U] == 1)
            {
                starts.push_back({ index, index + 4U });
                index += 4U;
                continue;
            }

            ++index;
        }

        std::vector<annex_b_nal> nals;
        for (std::size_t index = 0; index < starts.size(); ++index)
        {
            const std::size_t end =
                index + 1U < starts.size() ? starts[index + 1U].start : bytes.size();
            if (starts[index].header < end)
            {
                nals.push_back({ starts[index].start, starts[index].header, end });
            }
        }

        return nals;
    }
}

namespace ddc
{
    normalized_h264_access_unit h264_access_unit_normalizer::normalize(
        const std::span<const std::uint8_t> bytes)
    {
        const std::vector<annex_b_nal> nals = find_annex_b_nals(bytes);
        bool has_sequence_parameter_set{};
        bool has_picture_parameter_set{};
        bool has_idr{};
        for (const annex_b_nal& nal : nals)
        {
            const std::uint8_t type = bytes[nal.header] & 0x1FU;
            if (type == 7)
            {
                sequence_parameter_set_.assign(
                    bytes.begin() + static_cast<std::ptrdiff_t>(nal.start),
                    bytes.begin() + static_cast<std::ptrdiff_t>(nal.end));
                has_sequence_parameter_set = true;
            }
            else if (type == 8)
            {
                picture_parameter_set_.assign(
                    bytes.begin() + static_cast<std::ptrdiff_t>(nal.start),
                    bytes.begin() + static_cast<std::ptrdiff_t>(nal.end));
                has_picture_parameter_set = true;
            }
            else if (type == 5)
            {
                has_idr = true;
            }
        }

        normalized_h264_access_unit result;
        result.is_random_access_point = has_idr;
        const bool prepend_sequence_parameter_set =
            has_idr && !has_sequence_parameter_set && !sequence_parameter_set_.empty();
        const bool prepend_picture_parameter_set =
            has_idr && !has_picture_parameter_set && !picture_parameter_set_.empty();
        result.bytes.reserve(
            bytes.size() +
            (prepend_sequence_parameter_set ? sequence_parameter_set_.size() : 0U) +
            (prepend_picture_parameter_set ? picture_parameter_set_.size() : 0U));
        if (prepend_sequence_parameter_set)
        {
            result.bytes.insert(
                result.bytes.end(),
                sequence_parameter_set_.begin(),
                sequence_parameter_set_.end());
        }

        if (prepend_picture_parameter_set)
        {
            result.bytes.insert(
                result.bytes.end(),
                picture_parameter_set_.begin(),
                picture_parameter_set_.end());
        }

        result.bytes.insert(result.bytes.end(), bytes.begin(), bytes.end());
        return result;
    }
}
