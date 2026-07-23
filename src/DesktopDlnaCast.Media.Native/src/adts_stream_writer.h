#pragma once

#include "bounded_packet_queue.h"
#include "ddc_media.h"
#include "media_foundation_aac_encoder.h"

#include <functional>

namespace ddc
{
    using audio_packet_callback = std::function<void(media_packet packet)>;

    class adts_stream_writer final
    {
    public:
        explicit adts_stream_writer(audio_packet_callback callback);
        void write(aac_encoded_sample sample);

    private:
        audio_packet_callback callback_;
    };
}
