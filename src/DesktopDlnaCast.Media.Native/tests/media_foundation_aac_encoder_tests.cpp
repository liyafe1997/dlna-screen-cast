#include "media_foundation_aac_encoder.h"
#include "test_assert.h"

#include <cstdint>
#include <utility>
#include <vector>

void run_media_foundation_aac_encoder_tests()
{
    std::vector<ddc::aac_encoded_sample> samples;
    {
        ddc::media_foundation_aac_encoder encoder(
            128'000,
            [&samples](ddc::aac_encoded_sample sample)
            {
                samples.push_back(std::move(sample));
            });
        std::vector<std::int16_t> silence(
            static_cast<std::size_t>(ddc::media_foundation_aac_encoder::samples_per_frame) *
                ddc::media_foundation_aac_encoder::channels,
            0);
        for (std::int64_t frame = 0; frame < 12; ++frame)
        {
            encoder.encode(
                silence,
                frame * ddc::media_foundation_aac_encoder::samples_per_frame);
        }

        encoder.drain();
        DDC_TEST_CHECK(!samples.empty());
        DDC_TEST_CHECK(!encoder.codec_configuration().empty());
        DDC_TEST_CHECK(encoder.accepted_bitrate() > 0);
        DDC_TEST_CHECK(!encoder.encoder_name().empty());
    }
    for (std::size_t index = 0; index < samples.size(); ++index)
    {
        DDC_TEST_CHECK(!samples[index].bytes.empty());
        DDC_TEST_CHECK(samples[index].duration_100ns > 0);
        if (index > 0)
        {
            DDC_TEST_CHECK(samples[index].timestamp_100ns >= samples[index - 1].timestamp_100ns);
        }
    }
}
