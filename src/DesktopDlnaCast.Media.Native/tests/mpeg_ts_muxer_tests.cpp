#include "mpeg_ts_muxer.h"
#include "test_assert.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <utility>
#include <vector>

void run_mpeg_ts_muxer_tests()
{
    {
        std::vector<ddc::media_packet> output;
        ddc::mpeg_ts_muxer muxer(
            true,
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

    {
        std::vector<ddc::media_packet> output;
        constexpr std::array<std::uint8_t, 2> audio_specific_config{ 0x11, 0x90 };
        ddc::mpeg_ts_muxer muxer(
            false,
            0,
            0,
            0,
            {},
            128'000,
            audio_specific_config,
            [&output](ddc::media_packet packet) { output.push_back(std::move(packet)); });
        constexpr std::array<std::uint8_t, 8> encoded_aac_sample{
            0x21, 0x10, 0x04, 0x60, 0x8C, 0x1C, 0x00, 0x00,
        };
        for (std::int64_t index = 0; index < 10; ++index)
        {
            muxer.write_audio({
                encoded_aac_sample,
                index * 213'333,
                213'333,
            });
        }
        muxer.finish();

        DDC_TEST_CHECK(!output.empty());
        DDC_TEST_CHECK((output.front().flags & DDC_PACKET_FLAG_RANDOM_ACCESS_POINT) != 0);
        DDC_TEST_CHECK(output.front().bytes.size() % 188 == 0);
        DDC_TEST_CHECK(output.front().bytes.front() == 0x47);
    }
}
