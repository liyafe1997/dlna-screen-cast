#include "adts_stream_writer.h"

#include <algorithm>
#include <stdexcept>
#include <utility>

namespace
{
    constexpr std::uint8_t aac_lc_profile = 1;
    constexpr std::uint8_t sample_rate_48khz_index = 3;
    constexpr std::uint8_t stereo_channels = 2;
    constexpr std::size_t adts_header_size = 7;
    constexpr std::size_t maximum_adts_frame_size = 0x1FFF;
}

namespace ddc
{
    adts_stream_writer::adts_stream_writer(audio_packet_callback callback)
        : callback_(std::move(callback))
    {
        if (!callback_)
        {
            throw std::invalid_argument("The ADTS packet callback is required.");
        }
    }

    void adts_stream_writer::write(aac_encoded_sample sample)
    {
        const std::size_t frame_size = adts_header_size + sample.bytes.size();
        if (sample.bytes.empty() || frame_size > maximum_adts_frame_size ||
            sample.timestamp_100ns < 0 || sample.duration_100ns <= 0)
        {
            throw std::invalid_argument("The encoded AAC sample cannot be represented as ADTS.");
        }

        std::vector<std::uint8_t> bytes(frame_size);
        bytes[0] = 0xFF;
        bytes[1] = 0xF1; // MPEG-4, no CRC.
        bytes[2] = static_cast<std::uint8_t>(
            (aac_lc_profile << 6) |
            (sample_rate_48khz_index << 2) |
            (stereo_channels >> 2));
        bytes[3] = static_cast<std::uint8_t>(
            ((stereo_channels & 3) << 6) |
            ((frame_size >> 11) & 3));
        bytes[4] = static_cast<std::uint8_t>((frame_size >> 3) & 0xFF);
        bytes[5] = static_cast<std::uint8_t>(((frame_size & 7) << 5) | 0x1F);
        bytes[6] = 0xFC;
        std::copy(sample.bytes.begin(), sample.bytes.end(), bytes.begin() + adts_header_size);
        callback_({
            std::move(bytes),
            sample.timestamp_100ns,
            DDC_PACKET_FLAG_RANDOM_ACCESS_POINT,
        });
    }
}
