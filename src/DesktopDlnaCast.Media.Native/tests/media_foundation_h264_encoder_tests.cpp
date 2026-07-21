#include "media_foundation_h264_encoder.h"
#include "mpeg_ts_muxer.h"
#include "test_assert.h"

#include <algorithm>
#include <cstdint>
#include <iostream>
#include <optional>
#include <span>
#include <utility>
#include <vector>

namespace
{
    struct transport_stream_evidence final
    {
        std::optional<std::uint16_t> program_map_pid;
        std::optional<std::uint16_t> video_pid;
        std::optional<std::int64_t> last_video_pts;
        std::optional<std::int64_t> current_video_pts;
        std::optional<std::int64_t> last_idr_pts;
        std::uint64_t video_pts_count{};
        std::uint64_t idr_count{};
        std::int64_t maximum_idr_interval_90khz{};
        std::int32_t h264_zero_run{};
        bool pat_seen{};
        bool pmt_seen{};
        bool h264_seen{};
        bool sps_seen{};
        bool pps_seen{};
        bool awaiting_nal_header{};
        bool pts_monotonic{ true };
    };

    [[nodiscard]] std::int64_t read_pts(const std::span<const std::uint8_t, 5> bytes)
    {
        return (static_cast<std::int64_t>(bytes[0] & 0x0EU) << 29) |
            (static_cast<std::int64_t>(bytes[1]) << 22) |
            (static_cast<std::int64_t>(bytes[2] & 0xFEU) << 14) |
            (static_cast<std::int64_t>(bytes[3]) << 7) |
            (static_cast<std::int64_t>(bytes[4] & 0xFEU) >> 1);
    }

    void inspect_program_association_table(
        const std::span<const std::uint8_t> payload,
        transport_stream_evidence& evidence)
    {
        if (payload.empty())
        {
            return;
        }

        const std::size_t section_offset = 1U + payload[0];
        if (section_offset + 12U > payload.size() || payload[section_offset] != 0x00)
        {
            return;
        }

        const std::size_t section_length =
            (static_cast<std::size_t>(payload[section_offset + 1U] & 0x0FU) << 8) |
            payload[section_offset + 2U];
        const std::size_t section_end = section_offset + 3U + section_length;
        if (section_length < 9U || section_end > payload.size())
        {
            return;
        }

        const std::size_t entries_end = section_end - 4U;
        for (std::size_t offset = section_offset + 8U; offset + 4U <= entries_end; offset += 4U)
        {
            const std::uint16_t program_number = static_cast<std::uint16_t>(
                (static_cast<std::uint16_t>(payload[offset]) << 8) | payload[offset + 1U]);
            if (program_number == 0)
            {
                continue;
            }

            evidence.program_map_pid = static_cast<std::uint16_t>(
                (static_cast<std::uint16_t>(payload[offset + 2U] & 0x1FU) << 8) |
                payload[offset + 3U]);
            evidence.pat_seen = true;
            return;
        }
    }

    void inspect_program_map_table(
        const std::span<const std::uint8_t> payload,
        transport_stream_evidence& evidence)
    {
        if (payload.empty())
        {
            return;
        }

        const std::size_t section_offset = 1U + payload[0];
        if (section_offset + 16U > payload.size() || payload[section_offset] != 0x02)
        {
            return;
        }

        const std::size_t section_length =
            (static_cast<std::size_t>(payload[section_offset + 1U] & 0x0FU) << 8) |
            payload[section_offset + 2U];
        const std::size_t section_end = section_offset + 3U + section_length;
        if (section_length < 13U || section_end > payload.size())
        {
            return;
        }

        const std::size_t program_info_length =
            (static_cast<std::size_t>(payload[section_offset + 10U] & 0x0FU) << 8) |
            payload[section_offset + 11U];
        std::size_t stream_offset = section_offset + 12U + program_info_length;
        const std::size_t streams_end = section_end - 4U;
        while (stream_offset + 5U <= streams_end)
        {
            const std::uint8_t stream_type = payload[stream_offset];
            const std::uint16_t elementary_pid = static_cast<std::uint16_t>(
                (static_cast<std::uint16_t>(payload[stream_offset + 1U] & 0x1FU) << 8) |
                payload[stream_offset + 2U]);
            const std::size_t elementary_info_length =
                (static_cast<std::size_t>(payload[stream_offset + 3U] & 0x0FU) << 8) |
                payload[stream_offset + 4U];
            if (stream_type == 0x1B)
            {
                evidence.video_pid = elementary_pid;
                evidence.h264_seen = true;
            }

            stream_offset += 5U + elementary_info_length;
        }

        evidence.pmt_seen = true;
    }

    void observe_nal(const std::uint8_t nal_type, transport_stream_evidence& evidence)
    {
        switch (nal_type)
        {
        case 5:
            ++evidence.idr_count;
            if (evidence.current_video_pts)
            {
                if (evidence.last_idr_pts)
                {
                    evidence.maximum_idr_interval_90khz = std::max(
                        evidence.maximum_idr_interval_90khz,
                        *evidence.current_video_pts - *evidence.last_idr_pts);
                }

                evidence.last_idr_pts = evidence.current_video_pts;
            }

            break;
        case 7:
            evidence.sps_seen = true;
            break;
        case 8:
            evidence.pps_seen = true;
            break;
        default:
            break;
        }
    }

    void scan_h264(
        const std::span<const std::uint8_t> bytes,
        transport_stream_evidence& evidence)
    {
        for (const std::uint8_t value : bytes)
        {
            if (evidence.awaiting_nal_header)
            {
                observe_nal(value & 0x1FU, evidence);
                evidence.awaiting_nal_header = false;
            }

            if (value == 0)
            {
                evidence.h264_zero_run = std::min(evidence.h264_zero_run + 1, 3);
                continue;
            }

            if (value == 1 && evidence.h264_zero_run >= 2)
            {
                evidence.awaiting_nal_header = true;
            }

            evidence.h264_zero_run = 0;
        }
    }

    void inspect_transport_packet(
        const std::span<const std::uint8_t, 188> packet,
        transport_stream_evidence& evidence)
    {
        DDC_TEST_CHECK(packet[0] == 0x47);
        const bool payload_unit_start = (packet[1] & 0x40U) != 0;
        const std::uint16_t pid = static_cast<std::uint16_t>(
            (static_cast<std::uint16_t>(packet[1] & 0x1FU) << 8) | packet[2]);
        const std::uint8_t adaptation_control = (packet[3] >> 4) & 0x03U;
        DDC_TEST_CHECK(adaptation_control != 0);
        if ((adaptation_control & 0x01U) == 0)
        {
            return;
        }

        std::size_t payload_offset = 4U;
        if ((adaptation_control & 0x02U) != 0)
        {
            payload_offset += 1U + packet[4];
        }

        if (payload_offset >= packet.size())
        {
            return;
        }

        const auto payload = packet.subspan(payload_offset);
        if (payload_unit_start && pid == 0)
        {
            inspect_program_association_table(payload, evidence);
            return;
        }

        if (payload_unit_start && evidence.program_map_pid && pid == *evidence.program_map_pid)
        {
            inspect_program_map_table(payload, evidence);
            return;
        }

        if (!evidence.video_pid || pid != *evidence.video_pid)
        {
            return;
        }

        std::size_t elementary_offset{};
        if (payload_unit_start)
        {
            if (payload.size() < 14U || payload[0] != 0 || payload[1] != 0 || payload[2] != 1)
            {
                return;
            }

            const std::size_t header_length = payload[8];
            elementary_offset = 9U + header_length;
            if (elementary_offset > payload.size())
            {
                return;
            }

            if ((payload[7] & 0x80U) != 0 && header_length >= 5U)
            {
                const auto pts = read_pts(
                    std::span<const std::uint8_t, 5>(payload.subspan<9U, 5U>()));
                if (evidence.last_video_pts && pts < *evidence.last_video_pts)
                {
                    evidence.pts_monotonic = false;
                }

                evidence.last_video_pts = pts;
                evidence.current_video_pts = pts;
                ++evidence.video_pts_count;
            }
        }

        scan_h264(payload.subspan(elementary_offset), evidence);
    }

    [[nodiscard]] std::vector<std::uint8_t> create_nv12_frame(
        const std::uint32_t width,
        const std::uint32_t height)
    {
        std::vector<std::uint8_t> pixels(
            static_cast<std::size_t>(width) * height * 3U / 2U,
            128);
        std::fill_n(pixels.begin(), static_cast<std::size_t>(width) * height, 64);

        return pixels;
    }
}

void run_media_foundation_h264_encoder_tests()
{
    constexpr std::int32_t width = 320;
    constexpr std::int32_t height = 180;
    constexpr std::int32_t frame_rate = 30;
    constexpr std::int64_t frame_duration_100ns = 10'000'000LL / frame_rate;

    {
        ddc::media_foundation_h264_encoder selected_encoder(
            width,
            height,
            frame_rate,
            500'000,
            frame_rate,
            [](ddc::h264_encoded_sample) {});
        DDC_TEST_CHECK(!selected_encoder.encoder_name().empty());
        DDC_TEST_CHECK(
            selected_encoder.encoder_name().find(
                selected_encoder.is_hardware() ? "hardware" : "software") !=
            std::string::npos);
    }

    const auto frame_bytes = create_nv12_frame(width, height);
    std::vector<ddc::h264_encoded_sample> samples;
    std::vector<std::uint8_t> codec_configuration;
    {
        ddc::media_foundation_h264_encoder encoder(
            width,
            height,
            frame_rate,
            500'000,
            frame_rate,
            [&samples](ddc::h264_encoded_sample sample)
            {
                samples.push_back(std::move(sample));
            },
            ddc::h264_encoder_preference::software_only);
        const auto& diagnostics = encoder.diagnostics();
        DDC_TEST_CHECK(!diagnostics.is_hardware);
        DDC_TEST_CHECK(diagnostics.accepted_width == width);
        DDC_TEST_CHECK(diagnostics.accepted_height == height);
        DDC_TEST_CHECK(diagnostics.frame_rate_numerator == frame_rate);
        DDC_TEST_CHECK(diagnostics.frame_rate_denominator == 1);
        DDC_TEST_CHECK(diagnostics.accepted_video_bitrate == 500'000);
        DDC_TEST_CHECK(diagnostics.h264_profile > 0);
        DDC_TEST_CHECK(diagnostics.accepted_gop_frames == frame_rate);
        DDC_TEST_CHECK(diagnostics.accepted_b_frame_count == 0);
        for (std::int32_t frame = 0; frame < frame_rate + 5; ++frame)
        {
            encoder.encode(frame_bytes, static_cast<std::int64_t>(frame) * frame_duration_100ns);
        }

        encoder.drain();
        DDC_TEST_CHECK(!encoder.encoder_name().empty());
        codec_configuration = encoder.codec_configuration();
    }

    DDC_TEST_CHECK(!samples.empty());
    const auto key_frame_count = std::ranges::count_if(
        samples,
        [](const ddc::h264_encoded_sample& sample) { return sample.key_frame; });
    DDC_TEST_CHECK(key_frame_count >= 2);
    for (std::size_t index = 0; index < samples.size(); ++index)
    {
        DDC_TEST_CHECK(!samples[index].bytes.empty());
        DDC_TEST_CHECK(samples[index].duration_100ns > 0);
        if (index > 0)
        {
            if (samples[index].timestamp_100ns < samples[index - 1].timestamp_100ns)
            {
                std::cerr << "Non-monotonic encoder timestamp at sample " << index
                          << ": previous=" << samples[index - 1].timestamp_100ns
                          << " current=" << samples[index].timestamp_100ns << '\n';
            }

            DDC_TEST_CHECK(samples[index].timestamp_100ns >= samples[index - 1].timestamp_100ns);
        }
    }

    std::vector<ddc::media_packet> transport_stream_packets;
    ddc::mpeg_ts_muxer muxer(
        width,
        height,
        500'000,
        codec_configuration,
        0,
        {},
        [&transport_stream_packets](ddc::media_packet packet)
        {
            transport_stream_packets.push_back(std::move(packet));
        });
    for (const auto& sample : samples)
    {
        muxer.write_video({
            std::span<const std::uint8_t>(sample.bytes),
            sample.timestamp_100ns,
            sample.duration_100ns,
            sample.key_frame,
        });
    }

    muxer.finish();
    DDC_TEST_CHECK(!transport_stream_packets.empty());
    DDC_TEST_CHECK(std::ranges::any_of(
        transport_stream_packets,
        [](const ddc::media_packet& packet)
        {
            return (packet.flags & DDC_PACKET_FLAG_RANDOM_ACCESS_POINT) != 0;
        }));
    transport_stream_evidence evidence;
    for (const auto& packet : transport_stream_packets)
    {
        DDC_TEST_CHECK(!packet.bytes.empty());
        DDC_TEST_CHECK(packet.bytes.size() % 188U == 0);
        for (std::size_t offset = 0; offset < packet.bytes.size(); offset += 188U)
        {
            inspect_transport_packet(
                std::span<const std::uint8_t, 188>(packet.bytes.data() + offset, 188U),
                evidence);
        }
    }

    DDC_TEST_CHECK(evidence.pat_seen);
    DDC_TEST_CHECK(evidence.pmt_seen);
    DDC_TEST_CHECK(evidence.h264_seen);
    DDC_TEST_CHECK(evidence.sps_seen);
    DDC_TEST_CHECK(evidence.pps_seen);
    DDC_TEST_CHECK(evidence.idr_count >= 2U);
    DDC_TEST_CHECK(evidence.maximum_idr_interval_90khz > 0);
    DDC_TEST_CHECK(evidence.maximum_idr_interval_90khz <= 91'000);
    DDC_TEST_CHECK(evidence.pts_monotonic);
    DDC_TEST_CHECK(evidence.video_pts_count >= 30U);
}
