#include "mpeg_ts_muxer.h"

extern "C"
{
#include <libavutil/error.h>
#include <libavutil/mem.h>
#include <libavutil/mathematics.h>
}

#include <algorithm>
#include <array>
#include <cerrno>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <string>
#include <utility>

namespace
{
    constexpr std::size_t io_buffer_size = 32U * 1024U;
    constexpr std::size_t maximum_mux_chunk_bytes = 1024U * 1024U;
    constexpr std::size_t mpeg_ts_packet_size = 188;
    constexpr AVRational media_foundation_time_base{ 1, 10'000'000 };

    [[nodiscard]] std::runtime_error ffmpeg_error(const char* operation, const int result)
    {
        std::array<char, AV_ERROR_MAX_STRING_SIZE> message{};
        av_strerror(result, message.data(), message.size());
        return std::runtime_error(
            std::string(operation) + " failed: " + message.data() + " (" +
            std::to_string(result) + ").");
    }

    void check_ffmpeg(const int result, const char* operation)
    {
        if (result < 0)
        {
            throw ffmpeg_error(operation, result);
        }
    }

    void set_option(AVDictionary** options, const char* name, const char* value)
    {
        check_ffmpeg(av_dict_set(options, name, value, 0), "av_dict_set");
    }
}

namespace ddc
{
    mpeg_ts_muxer::mpeg_ts_muxer(
        const bool include_video,
        const std::int32_t width,
        const std::int32_t height,
        const std::int32_t bitrate,
        const std::span<const std::uint8_t> codec_configuration,
        const std::int32_t audio_bitrate,
        const std::span<const std::uint8_t> audio_codec_configuration,
        muxed_packet_callback callback)
        : callback_(std::move(callback))
    {
        if ((include_video &&
             (width < 2 || height < 2 || (width & 1) != 0 || (height & 1) != 0 ||
              bitrate <= 0)) ||
            audio_bitrate < 0 || (!include_video && audio_bitrate == 0) || !callback_)
        {
            throw std::invalid_argument("The MPEG-TS muxer configuration is invalid.");
        }

        try
        {
            check_ffmpeg(
                avformat_alloc_output_context2(&format_context_, nullptr, "mpegts", nullptr),
                "avformat_alloc_output_context2");
            if (format_context_ == nullptr)
            {
                throw std::runtime_error("FFmpeg did not allocate an MPEG-TS output context.");
            }

            has_video_ = include_video;
            if (include_video)
            {
                video_stream_ = avformat_new_stream(format_context_, nullptr);
                if (video_stream_ == nullptr)
                {
                    throw std::bad_alloc();
                }

                video_stream_->time_base = { 1, 90'000 };
                AVCodecParameters* parameters = video_stream_->codecpar;
                parameters->codec_type = AVMEDIA_TYPE_VIDEO;
                parameters->codec_id = AV_CODEC_ID_H264;
                parameters->codec_tag = 0;
                parameters->format = AV_PIX_FMT_YUV420P;
                parameters->width = width;
                parameters->height = height;
                parameters->bit_rate = bitrate;
                if (codec_configuration.size() >
                    static_cast<std::size_t>(
                        std::numeric_limits<int>::max() - AV_INPUT_BUFFER_PADDING_SIZE))
                {
                    throw std::length_error("The H.264 codec configuration is too large.");
                }

                if (!codec_configuration.empty())
                {
                    const auto allocation_size =
                        codec_configuration.size() + AV_INPUT_BUFFER_PADDING_SIZE;
                    parameters->extradata =
                        static_cast<std::uint8_t*>(av_mallocz(allocation_size));
                    if (parameters->extradata == nullptr)
                    {
                        throw std::bad_alloc();
                    }

                    std::memcpy(
                        parameters->extradata,
                        codec_configuration.data(),
                        codec_configuration.size());
                    parameters->extradata_size = static_cast<int>(codec_configuration.size());
                }
            }

            if (audio_bitrate > 0)
            {
                audio_stream_ = avformat_new_stream(format_context_, nullptr);
                if (audio_stream_ == nullptr)
                {
                    throw std::bad_alloc();
                }

                audio_stream_->time_base = { 1, 48'000 };
                AVCodecParameters* audio_parameters = audio_stream_->codecpar;
                audio_parameters->codec_type = AVMEDIA_TYPE_AUDIO;
                audio_parameters->codec_id = AV_CODEC_ID_AAC;
                audio_parameters->codec_tag = 0;
                audio_parameters->format = AV_SAMPLE_FMT_S16;
                audio_parameters->sample_rate = 48'000;
                audio_parameters->bit_rate = audio_bitrate;
                audio_parameters->frame_size = 1024;
                av_channel_layout_default(&audio_parameters->ch_layout, 2);
                if (audio_codec_configuration.size() >
                    static_cast<std::size_t>(
                        std::numeric_limits<int>::max() - AV_INPUT_BUFFER_PADDING_SIZE))
                {
                    throw std::length_error("The AAC codec configuration is too large.");
                }

                if (!audio_codec_configuration.empty())
                {
                    const auto allocation_size =
                        audio_codec_configuration.size() + AV_INPUT_BUFFER_PADDING_SIZE;
                    audio_parameters->extradata =
                        static_cast<std::uint8_t*>(av_mallocz(allocation_size));
                    if (audio_parameters->extradata == nullptr)
                    {
                        throw std::bad_alloc();
                    }

                    std::memcpy(
                        audio_parameters->extradata,
                        audio_codec_configuration.data(),
                        audio_codec_configuration.size());
                    audio_parameters->extradata_size =
                        static_cast<int>(audio_codec_configuration.size());
                }
            }

            auto* io_buffer = static_cast<std::uint8_t*>(av_malloc(io_buffer_size));
            if (io_buffer == nullptr)
            {
                throw std::bad_alloc();
            }

            io_context_ = avio_alloc_context(
                io_buffer,
                static_cast<int>(io_buffer_size),
                1,
                this,
                nullptr,
                &mpeg_ts_muxer::write_packet,
                nullptr);
            if (io_context_ == nullptr)
            {
                av_free(io_buffer);
                throw std::bad_alloc();
            }

            format_context_->pb = io_context_;
            format_context_->flags |= AVFMT_FLAG_CUSTOM_IO;
            format_context_->flags |= AVFMT_FLAG_FLUSH_PACKETS;
            format_context_->max_interleave_delta = 0;
            AVDictionary* options{};
            try
            {
                set_option(&options, "mpegts_flags", "resend_headers");
                set_option(&options, "muxdelay", "0");
                set_option(&options, "muxpreload", "0");
                set_option(&options, "pat_period", "0.1");
                const int header_result = avformat_write_header(format_context_, &options);
                av_dict_free(&options);
                check_ffmpeg(header_result, "avformat_write_header");
            }
            catch (...)
            {
                av_dict_free(&options);
                throw;
            }
            throw_if_callback_failed();
            header_bytes_ = std::move(current_bytes_);
            current_bytes_.clear();
        }
        catch (...)
        {
            close();
            throw;
        }
    }

    mpeg_ts_muxer::~mpeg_ts_muxer()
    {
        close();
    }

    void mpeg_ts_muxer::write_video(const encoded_video_sample& sample)
    {
        if (sample.bytes.empty() ||
            sample.bytes.size() > static_cast<std::size_t>(std::numeric_limits<int>::max()) ||
            sample.duration_100ns <= 0 || sample.timestamp_100ns < 0)
        {
            throw std::invalid_argument("An encoded H.264 sample is invalid.");
        }

        std::scoped_lock lock(mutex_);
        if (finished_ || format_context_ == nullptr)
        {
            throw std::logic_error("The MPEG-TS muxer is already finished.");
        }

        AVPacket* packet = av_packet_alloc();
        if (packet == nullptr)
        {
            throw std::bad_alloc();
        }

        try
        {
            check_ffmpeg(
                av_new_packet(packet, static_cast<int>(sample.bytes.size())),
                "av_new_packet");
            std::memcpy(packet->data, sample.bytes.data(), sample.bytes.size());
            packet->stream_index = video_stream_->index;
            packet->pts = av_rescale_q(
                sample.timestamp_100ns,
                media_foundation_time_base,
                video_stream_->time_base);
            packet->dts = packet->pts;
            packet->duration = av_rescale_q(
                sample.duration_100ns,
                media_foundation_time_base,
                video_stream_->time_base);
            if (sample.key_frame)
            {
                packet->flags |= AV_PKT_FLAG_KEY;
            }

            current_bytes_.clear();
            check_ffmpeg(
                has_video_
                    ? av_interleaved_write_frame(format_context_, packet)
                    : av_write_frame(format_context_, packet),
                has_video_ ? "av_interleaved_write_frame" : "av_write_frame");
            avio_flush(io_context_);
            throw_if_callback_failed();
            if (sample.key_frame && !header_bytes_.empty())
            {
                if (current_bytes_.size() + header_bytes_.size() > maximum_mux_chunk_bytes)
                {
                    throw std::length_error("The MPEG-TS startup chunk is too large.");
                }

                current_bytes_.insert(
                    current_bytes_.begin(),
                    header_bytes_.begin(),
                    header_bytes_.end());
            }

            emit_current(sample.key_frame, sample.timestamp_100ns);
        }
        catch (...)
        {
            av_packet_free(&packet);
            throw;
        }

        av_packet_free(&packet);
    }

    void mpeg_ts_muxer::write_audio(const encoded_audio_sample& sample)
    {
        if (audio_stream_ == nullptr || sample.bytes.empty() ||
            sample.bytes.size() > static_cast<std::size_t>(std::numeric_limits<int>::max()) ||
            sample.duration_100ns <= 0 || sample.timestamp_100ns < 0)
        {
            throw std::invalid_argument("An encoded AAC sample is invalid.");
        }

        std::scoped_lock lock(mutex_);
        if (finished_ || format_context_ == nullptr)
        {
            throw std::logic_error("The MPEG-TS muxer is already finished.");
        }

        AVPacket* packet = av_packet_alloc();
        if (packet == nullptr)
        {
            throw std::bad_alloc();
        }

        try
        {
            check_ffmpeg(
                av_new_packet(packet, static_cast<int>(sample.bytes.size())),
                "av_new_packet");
            std::memcpy(packet->data, sample.bytes.data(), sample.bytes.size());
            packet->stream_index = audio_stream_->index;
            packet->pts = av_rescale_q(
                sample.timestamp_100ns,
                media_foundation_time_base,
                audio_stream_->time_base);
            packet->dts = packet->pts;
            packet->duration = av_rescale_q(
                sample.duration_100ns,
                media_foundation_time_base,
                audio_stream_->time_base);
            current_bytes_.clear();
            check_ffmpeg(
                has_video_
                    ? av_interleaved_write_frame(format_context_, packet)
                    : av_write_frame(format_context_, packet),
                has_video_ ? "av_interleaved_write_frame" : "av_write_frame");
            avio_flush(io_context_);
            throw_if_callback_failed();
            if (!has_video_ && !header_bytes_.empty())
            {
                if (current_bytes_.size() + header_bytes_.size() > maximum_mux_chunk_bytes)
                {
                    throw std::length_error("The MPEG-TS audio startup chunk is too large.");
                }

                current_bytes_.insert(
                    current_bytes_.begin(),
                    header_bytes_.begin(),
                    header_bytes_.end());
            }

            emit_current(!has_video_, sample.timestamp_100ns);
        }
        catch (...)
        {
            av_packet_free(&packet);
            throw;
        }

        av_packet_free(&packet);
    }

    void mpeg_ts_muxer::finish()
    {
        std::scoped_lock lock(mutex_);
        if (finished_)
        {
            return;
        }

        if (format_context_ != nullptr)
        {
            current_bytes_.clear();
            check_ffmpeg(av_write_trailer(format_context_), "av_write_trailer");
            avio_flush(io_context_);
            throw_if_callback_failed();
            emit_current(false, 0);
        }

        finished_ = true;
    }

    int mpeg_ts_muxer::write_packet(
        void* opaque,
        const std::uint8_t* buffer,
        const int buffer_size) noexcept
    {
        if (opaque == nullptr)
        {
            return AVERROR(EINVAL);
        }

        return static_cast<mpeg_ts_muxer*>(opaque)->append_output(buffer, buffer_size);
    }

    int mpeg_ts_muxer::append_output(
        const std::uint8_t* buffer,
        const int buffer_size) noexcept
    {
        if (buffer == nullptr || buffer_size <= 0)
        {
            return AVERROR(EINVAL);
        }

        try
        {
            if (current_bytes_.size() + static_cast<std::size_t>(buffer_size) >
                maximum_mux_chunk_bytes)
            {
                throw std::length_error("FFmpeg produced an oversized MPEG-TS chunk.");
            }

            current_bytes_.insert(current_bytes_.end(), buffer, buffer + buffer_size);
            return buffer_size;
        }
        catch (...)
        {
            callback_failure_ = std::current_exception();
            return AVERROR(EIO);
        }
    }

    void mpeg_ts_muxer::throw_if_callback_failed()
    {
        if (callback_failure_)
        {
            std::exception_ptr failure = std::exchange(callback_failure_, nullptr);
            std::rethrow_exception(failure);
        }
    }

    void mpeg_ts_muxer::emit_current(
        const bool random_access_point,
        const std::int64_t timestamp_100ns)
    {
        if (current_bytes_.empty())
        {
            return;
        }

        if (current_bytes_.size() % mpeg_ts_packet_size != 0)
        {
            throw std::runtime_error("FFmpeg produced an MPEG-TS chunk that is not packet aligned.");
        }

        media_packet packet{
            std::move(current_bytes_),
            timestamp_100ns,
            random_access_point ? DDC_PACKET_FLAG_RANDOM_ACCESS_POINT : 0,
        };
        current_bytes_.clear();
        callback_(std::move(packet));
    }

    void mpeg_ts_muxer::close() noexcept
    {
        try
        {
            if (!finished_ && format_context_ != nullptr)
            {
                av_write_trailer(format_context_);
            }
        }
        catch (...)
        {
        }

        finished_ = true;
        if (format_context_ != nullptr)
        {
            format_context_->pb = nullptr;
        }

        if (io_context_ != nullptr)
        {
            avio_context_free(&io_context_);
        }

        if (format_context_ != nullptr)
        {
            avformat_free_context(format_context_);
            format_context_ = nullptr;
        }

        video_stream_ = nullptr;
        audio_stream_ = nullptr;
    }
}
