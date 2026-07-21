#include "mpeg_ts_muxer.h"
#include "test_assert.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <utility>
#include <vector>

void run_mpeg_ts_muxer_tests()
{
    std::vector<ddc::media_packet> output;
    ddc::mpeg_ts_muxer muxer(
        1280,
        720,
        3'000'000,
        {},
        0,
        {},
        [&output](ddc::media_packet packet) { output.push_back(std::move(packet)); });
    // Annex-B-shaped SPS/PPS/IDR bytes are sufficient for exercising libavformat's
    // MPEG-TS packetization here. Decode validity is covered by StreamProbe fixtures.
    constexpr std::array<std::uint8_t, 35> encoded_sample{
        0x00, 0x00, 0x00, 0x01, 0x67, 0x4D, 0x40, 0x1F, 0x96,
        0x54, 0x05, 0x01, 0xED, 0x00, 0xF0, 0x88, 0x45, 0x80,
        0x00, 0x00, 0x00, 0x01, 0x68, 0xEE, 0x3C, 0x80,
        0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x84, 0x00, 0x10,
    };
    muxer.write_video({ encoded_sample, 0, 333'333, true });
    muxer.finish();
    muxer.finish();

    DDC_TEST_CHECK(!output.empty());
    DDC_TEST_CHECK((output.front().flags & DDC_PACKET_FLAG_RANDOM_ACCESS_POINT) != 0);
    for (const auto& packet : output)
    {
        DDC_TEST_CHECK(!packet.bytes.empty());
        DDC_TEST_CHECK(packet.bytes.size() % 188 == 0);
        for (std::size_t offset = 0; offset < packet.bytes.size(); offset += 188)
        {
            DDC_TEST_CHECK(packet.bytes[offset] == 0x47);
        }
    }
}
