#include "h264_access_unit_normalizer.h"
#include "test_assert.h"

#include <array>
#include <cstdint>

void run_h264_access_unit_normalizer_tests()
{
    ddc::h264_access_unit_normalizer normalizer;
    constexpr std::array<std::uint8_t, 16> parameters{
        0x00, 0x00, 0x00, 0x01, 0x67, 0x4D, 0x40, 0x1F,
        0x00, 0x00, 0x00, 0x01, 0x68, 0xEE, 0x3C, 0x80,
    };
    const auto parameter_result = normalizer.normalize(parameters);
    DDC_TEST_CHECK(!parameter_result.is_random_access_point);

    constexpr std::array<std::uint8_t, 8> idr{
        0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x10,
    };
    const auto idr_result = normalizer.normalize(idr);
    DDC_TEST_CHECK(idr_result.is_random_access_point);
    DDC_TEST_CHECK(idr_result.bytes.size() == parameters.size() + idr.size());
    DDC_TEST_CHECK(std::equal(
        parameters.begin(),
        parameters.end(),
        idr_result.bytes.begin()));

    constexpr std::array<std::uint8_t, 8> non_idr{
        0x00, 0x00, 0x00, 0x01, 0x61, 0x9A, 0x20, 0x10,
    };
    const auto non_idr_result = normalizer.normalize(non_idr);
    DDC_TEST_CHECK(!non_idr_result.is_random_access_point);

    constexpr std::array<std::uint8_t, 24> complete_idr{
        0x00, 0x00, 0x00, 0x01, 0x67, 0x4D, 0x40, 0x1F,
        0x00, 0x00, 0x00, 0x01, 0x68, 0xEE, 0x3C, 0x80,
        0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x10,
    };
    const auto complete_result = normalizer.normalize(complete_idr);
    DDC_TEST_CHECK(complete_result.is_random_access_point);
    DDC_TEST_CHECK(complete_result.bytes.size() == complete_idr.size());
}
