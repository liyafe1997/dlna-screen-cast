#include "media_session.h"

#include <algorithm>
#include <chrono>
#include <cstring>
#include <exception>
#include <limits>
#include <span>
#include <stdexcept>
#include <utility>
#include <vector>

namespace
{
    constexpr std::int32_t mpeg_ts_continuous_mode = 1;

    [[nodiscard]] std::string describe_exception(const std::exception_ptr& failure)
    {
        if (!failure)
        {
            return "The native media pipeline failed without exception details.";
        }

        try
        {
            std::rethrow_exception(failure);
        }
        catch (const winrt::hresult_error& error)
        {
            return "Windows media API failure: " + winrt::to_string(error.message()) +
                " (HRESULT " + std::to_string(error.code().value) + ").";
        }
        catch (const std::exception& error)
        {
            return std::string("Native media pipeline failure: ") + error.what();
        }
        catch (...)
        {
            return "The native media pipeline failed with an unknown exception.";
        }
    }
}

namespace ddc
{
    media_session::media_session(const ddc_stream_config& config) : config_(config)
    {
        statistics_.struct_size = static_cast<std::int32_t>(sizeof(ddc_session_statistics));
        statistics_.abi_version = DDC_ABI_VERSION;
    }

    media_session::~media_session()
    {
        try
        {
            static_cast<void>(stop());
        }
        catch (...)
        {
            cleanup_pipeline();
            output_packets_.stop();
        }
    }

    std::int32_t media_session::start()
    {
        std::scoped_lock operation_lock(operation_mutex_);
        {
            std::scoped_lock lock(mutex_);
            if (state_ == state::running)
            {
                return DDC_OK;
            }

            if (state_ != state::created)
            {
                set_error_locked("A stopped or failed native media session cannot be restarted.");
                return DDC_E_INVALID_STATE;
            }

            state_ = state::starting;
            last_error_.clear();
        }

        try
        {
            session_start_timestamp_100ns_ = wasapi_loopback_capture::monotonic_time_100ns();
            next_output_timestamp_100ns_ = 0;
            video_processor_backend_ = video_processor_backend::undecided;
            video_processor_fallback_reason_.clear();
            if (config_.audio_only != 0)
            {
                bool started{};
                {
                    std::scoped_lock lock(mutex_);
                    if (state_ == state::starting)
                    {
                        state_ = state::running;
                        started = true;
                    }
                }

                if (!started)
                {
                    cleanup_pipeline();
                    return DDC_E_MEDIA_PIPELINE;
                }

                return DDC_OK;
            }

            video_processor_ = std::make_unique<d3d11_video_processor>(
                config_.width,
                config_.height,
                config_.frame_rate);
            capture_ = std::make_unique<graphics_capture_source>();
            capture_->start(
                config_,
                [this](
                    const winrt::com_ptr<ID3D11Texture2D>& texture,
                    const std::int32_t width,
                    const std::int32_t height,
                    const std::int64_t timestamp_100ns,
                    const bool is_repeated_frame)
                {
                    try
                    {
                        process_captured_frame(
                            texture,
                            width,
                            height,
                            timestamp_100ns,
                            is_repeated_frame);
                    }
                    catch (...)
                    {
                        record_pipeline_failure(std::current_exception());
                        throw;
                    }
                },
                [this](std::exception_ptr failure)
                {
                    record_pipeline_failure(std::move(failure));
                });

            {
                std::scoped_lock lock(mutex_);
                if (state_ == state::starting)
                {
                    state_ = state::running;
                    return DDC_OK;
                }
            }

            cleanup_pipeline();
            return DDC_E_MEDIA_PIPELINE;
        }
        catch (...)
        {
            record_pipeline_failure(std::current_exception());
            cleanup_pipeline();
            output_packets_.stop();
            std::scoped_lock lock(mutex_);
            state_ = state::failed;
            return DDC_E_MEDIA_PIPELINE;
        }
    }

    void media_session::process_captured_frame(
        const winrt::com_ptr<ID3D11Texture2D>& texture,
        const std::int32_t width,
        const std::int32_t height,
        const std::int64_t timestamp_100ns,
        const bool is_repeated_frame)
    {
        if (!is_repeated_frame)
        {
            std::scoped_lock lock(mutex_);
            ++statistics_.captured_video_frames;
        }

        if (timestamp_100ns < 0)
        {
            std::scoped_lock lock(mutex_);
            ++statistics_.dropped_video_frames;
            return;
        }

        std::int64_t relative_timestamp = timestamp_100ns - session_start_timestamp_100ns_;
        if (relative_timestamp < 0)
        {
            relative_timestamp = next_output_timestamp_100ns_;
            std::scoped_lock lock(mutex_);
            ++statistics_.timestamp_corrections;
        }

        const std::int64_t frame_duration = 10'000'000LL / config_.frame_rate;
        if (relative_timestamp + frame_duration / 4 < next_output_timestamp_100ns_)
        {
            std::scoped_lock lock(mutex_);
            ++statistics_.dropped_video_frames;
            return;
        }

        if (relative_timestamp > next_output_timestamp_100ns_ + frame_duration * 3)
        {
            next_output_timestamp_100ns_ = relative_timestamp;
            std::scoped_lock lock(mutex_);
            ++statistics_.timestamp_corrections;
        }

        std::vector<std::uint8_t> processed_frame;
        if (video_processor_backend_ == video_processor_backend::undecided)
        {
            try
            {
                processed_frame = video_processor_->process(texture, width, height);
                video_processor_backend_ = video_processor_backend::d3d11;
            }
            catch (...)
            {
                video_processor_fallback_reason_ = describe_exception(std::current_exception());
                constexpr std::size_t maximum_fallback_reason_bytes = 240;
                if (video_processor_fallback_reason_.size() > maximum_fallback_reason_bytes)
                {
                    video_processor_fallback_reason_.resize(maximum_fallback_reason_bytes);
                }

                video_processor_.reset();
                try
                {
                    software_video_processor_ = std::make_unique<software_video_processor>(
                        config_.width,
                        config_.height);
                    processed_frame = software_video_processor_->process(texture, width, height);
                    video_processor_backend_ = video_processor_backend::software;
                }
                catch (...)
                {
                    throw std::runtime_error(
                        "D3D11 video processing failed (" + video_processor_fallback_reason_ +
                        "); the libswscale fallback also failed (" +
                        describe_exception(std::current_exception()) + ").");
                }
            }
        }
        else if (video_processor_backend_ == video_processor_backend::d3d11)
        {
            processed_frame = video_processor_->process(texture, width, height);
        }
        else
        {
            processed_frame = software_video_processor_->process(texture, width, height);
        }

        if (!video_encoder_)
        {
            create_encoder_and_muxer();
        }

        const auto encode_started = std::chrono::steady_clock::now();
        video_encoder_->encode(processed_frame, next_output_timestamp_100ns_);
        const auto encode_finished = std::chrono::steady_clock::now();
        const auto wait_100ns = std::chrono::duration_cast<std::chrono::nanoseconds>(
            encode_finished - encode_started).count() / 100;
        {
            std::scoped_lock lock(mutex_);
            statistics_.encoder_wait_100ns += wait_100ns;
        }

        next_output_timestamp_100ns_ += frame_duration;
    }

    void media_session::create_encoder_and_muxer()
    {
        ddc_encoder_diagnostics diagnostics{};
        diagnostics.struct_size = static_cast<std::int32_t>(sizeof(diagnostics));
        diagnostics.abi_version = DDC_ABI_VERSION;
        if (config_.audio_only == 0)
        {
            video_encoder_ = std::make_unique<media_foundation_h264_encoder>(
                config_.width,
                config_.height,
                config_.frame_rate,
                config_.video_bitrate,
                config_.gop_frames,
                [this](h264_encoded_sample sample)
                {
                    if (!muxer_)
                    {
                        throw std::logic_error("The MPEG-TS muxer is not initialized.");
                    }

                    muxer_->write_video({
                        std::span<const std::uint8_t>(sample.bytes),
                        sample.timestamp_100ns,
                        sample.duration_100ns,
                        sample.key_frame,
                    });
                    std::scoped_lock lock(mutex_);
                    ++statistics_.encoded_video_frames;
                });
            const auto& accepted = video_encoder_->diagnostics();
            diagnostics.is_hardware = accepted.is_hardware ? 1 : 0;
            diagnostics.accepted_width = accepted.accepted_width;
            diagnostics.accepted_height = accepted.accepted_height;
            diagnostics.frame_rate_numerator = accepted.frame_rate_numerator;
            diagnostics.frame_rate_denominator = accepted.frame_rate_denominator;
            diagnostics.accepted_video_bitrate = accepted.accepted_video_bitrate;
            diagnostics.h264_profile = accepted.h264_profile;
            diagnostics.accepted_gop_frames = accepted.accepted_gop_frames;
            diagnostics.accepted_b_frame_count = accepted.accepted_b_frame_count;
            diagnostics.video_processor_backend =
                video_processor_backend_ == video_processor_backend::d3d11
                    ? DDC_VIDEO_PROCESSOR_D3D11
                    : DDC_VIDEO_PROCESSOR_LIBSWSCALE;
        }

        std::string audio_fallback_reason;
        if (config_.include_audio != 0 &&
            config_.audio_only != 0 &&
            config_.audio_profile == DDC_AUDIO_PROFILE_MP3)
        {
            mp3_encoder_ = std::make_unique<media_foundation_mp3_encoder>(
                config_.audio_bitrate,
                [this](media_packet packet)
                {
                    static_cast<void>(output_packets_.push(std::move(packet)));
                    std::scoped_lock lock(mutex_);
                    ++statistics_.encoded_audio_frames;
                });
            diagnostics.audio_enabled = 1;
            diagnostics.accepted_audio_bitrate = mp3_encoder_->accepted_bitrate();
            diagnostics.audio_sample_rate = media_foundation_mp3_encoder::sample_rate;
            diagnostics.audio_channels = media_foundation_mp3_encoder::channels;
        }
        else if (config_.include_audio != 0 &&
                 config_.audio_only != 0 &&
                 config_.audio_profile == DDC_AUDIO_PROFILE_LPCM)
        {
            diagnostics.audio_enabled = 1;
            diagnostics.accepted_audio_bitrate = 1'536'000;
            diagnostics.audio_sample_rate = wasapi_loopback_capture::sample_rate;
            diagnostics.audio_channels = wasapi_loopback_capture::channels;
        }
        else if (config_.include_audio != 0)
        {
            try
            {
                audio_encoder_ = std::make_unique<media_foundation_aac_encoder>(
                    config_.audio_bitrate,
                    [this](aac_encoded_sample sample)
                    {
                        if (config_.audio_only != 0 &&
                            config_.audio_profile == DDC_AUDIO_PROFILE_AAC_ADTS)
                        {
                            if (!adts_writer_)
                            {
                                throw std::logic_error("The ADTS stream writer is not initialized.");
                            }

                            adts_writer_->write(std::move(sample));
                        }
                        else
                        {
                            if (!muxer_)
                            {
                                throw std::logic_error("The MPEG-TS muxer is not initialized.");
                            }

                            muxer_->write_audio({
                                std::span<const std::uint8_t>(sample.bytes),
                                sample.timestamp_100ns,
                                sample.duration_100ns,
                            });
                        }
                        std::scoped_lock lock(mutex_);
                        ++statistics_.encoded_audio_frames;
                    });
                diagnostics.audio_enabled = 1;
                diagnostics.accepted_audio_bitrate = audio_encoder_->accepted_bitrate();
                diagnostics.audio_sample_rate = media_foundation_aac_encoder::sample_rate;
                diagnostics.audio_channels = media_foundation_aac_encoder::channels;
            }
            catch (...)
            {
                if (config_.audio_only != 0)
                {
                    throw;
                }

                audio_fallback_reason = describe_exception(std::current_exception());
                constexpr std::size_t maximum_audio_reason_bytes = 240;
                if (audio_fallback_reason.size() > maximum_audio_reason_bytes)
                {
                    audio_fallback_reason.resize(maximum_audio_reason_bytes);
                }

                audio_encoder_.reset();
            }
        }

        if (mp3_encoder_ ||
            (config_.audio_only != 0 &&
             config_.audio_profile == DDC_AUDIO_PROFILE_LPCM))
        {
            // MP3 packets are already a self-framing HTTP audio stream.
        }
        else if (config_.audio_only != 0 &&
            config_.audio_profile == DDC_AUDIO_PROFILE_AAC_ADTS)
        {
            adts_writer_ = std::make_unique<adts_stream_writer>(
                [this](media_packet packet)
                {
                    static_cast<void>(output_packets_.push(std::move(packet)));
                });
        }
        else
        {
            muxer_ = std::make_unique<mpeg_ts_muxer>(
                config_.audio_only == 0,
                config_.width,
                config_.height,
                config_.video_bitrate,
                video_encoder_
                    ? std::span<const std::uint8_t>(video_encoder_->codec_configuration())
                    : std::span<const std::uint8_t>{},
                audio_encoder_ ? audio_encoder_->accepted_bitrate() : 0,
                audio_encoder_
                    ? std::span<const std::uint8_t>(audio_encoder_->codec_configuration())
                    : std::span<const std::uint8_t>{},
                [this](media_packet packet)
                {
                    static_cast<void>(output_packets_.push(std::move(packet)));
                });
        }

        {
            std::scoped_lock lock(mutex_);
            encoder_diagnostics_ = diagnostics;
            encoder_name_ = video_encoder_ ? video_encoder_->encoder_name() : "Audio-only";
            if (video_encoder_ && video_processor_backend_ == video_processor_backend::software)
            {
                encoder_name_ += "; libswscale pixel fallback: " +
                    video_processor_fallback_reason_;
            }

            if (audio_encoder_)
            {
                encoder_name_ += "; " + audio_encoder_->encoder_name();
            }
            else if (mp3_encoder_)
            {
                encoder_name_ += "; " + mp3_encoder_->encoder_name();
            }
            else if (config_.audio_only != 0 &&
                     config_.audio_profile == DDC_AUDIO_PROFILE_LPCM)
            {
                encoder_name_ += "; LPCM L16 48 kHz stereo";
            }
            else if (config_.include_audio != 0)
            {
                encoder_name_ += "; audio disabled after AAC fallback: " + audio_fallback_reason;
            }
        }

        if ((audio_encoder_ || mp3_encoder_) && config_.audio_only == 0)
        {
            start_audio_capture();
        }
    }

    void media_session::start_audio_capture()
    {
        if (audio_encoder_ || mp3_encoder_ ||
            (config_.audio_only != 0 &&
             config_.audio_profile == DDC_AUDIO_PROFILE_LPCM))
        {
            audio_capture_ = std::make_unique<wasapi_loopback_capture>();
            audio_capture_->start(
                session_start_timestamp_100ns_,
                config_.mute_local_playback != 0,
                [this](std::vector<std::int16_t> pcm, const std::int64_t start_sample)
                {
                    if (mp3_encoder_)
                    {
                        mp3_encoder_->encode(std::span<const std::int16_t>(pcm), start_sample);
                    }
                    else if (config_.audio_only != 0 &&
                             config_.audio_profile == DDC_AUDIO_PROFILE_LPCM)
                    {
                        std::vector<std::uint8_t> bytes(pcm.size() * sizeof(std::int16_t));
                        for (std::size_t index = 0; index < pcm.size(); ++index)
                        {
                            const auto sample = static_cast<std::uint16_t>(pcm[index]);
                            bytes[index * 2] = static_cast<std::uint8_t>(sample >> 8);
                            bytes[index * 2 + 1] = static_cast<std::uint8_t>(sample & 0xFF);
                        }

                        const std::int64_t timestamp_100ns =
                            start_sample * 10'000'000LL /
                            wasapi_loopback_capture::sample_rate;
                        static_cast<void>(output_packets_.push({
                            std::move(bytes),
                            timestamp_100ns,
                            DDC_PACKET_FLAG_RANDOM_ACCESS_POINT,
                        }));
                        std::scoped_lock lock(mutex_);
                        ++statistics_.encoded_audio_frames;
                    }
                    else
                    {
                        audio_encoder_->encode(std::span<const std::int16_t>(pcm), start_sample);
                    }
                },
                [this]
                {
                    std::scoped_lock lock(mutex_);
                    ++statistics_.captured_audio_packets;
                },
                [this]
                {
                    std::scoped_lock lock(mutex_);
                    ++statistics_.audio_device_changes;
                },
                [this]
                {
                    std::scoped_lock lock(mutex_);
                    ++statistics_.timestamp_corrections;
                },
                [this]
                {
                    std::scoped_lock lock(mutex_);
                    ++statistics_.local_playback_mute_changes;
                },
                [this]
                {
                    std::scoped_lock lock(mutex_);
                    ++statistics_.local_playback_mute_restore_failures;
                },
                [this](std::exception_ptr failure)
                {
                    record_pipeline_failure(std::move(failure));
                });
        }
    }

    void media_session::record_pipeline_failure(const std::exception_ptr failure) noexcept
    {
        try
        {
            const std::string message = describe_exception(failure);
            std::scoped_lock lock(mutex_);
            set_error_locked(message);
            if (state_ == state::running || state_ == state::starting)
            {
                state_ = state::failed;
            }
        }
        catch (...)
        {
        }

        output_packets_.stop();
    }

    std::int32_t media_session::read(
        std::uint8_t* buffer,
        const std::int32_t buffer_capacity,
        std::int32_t* bytes_written,
        std::int64_t* timestamp_100ns,
        std::int32_t* packet_flags,
        const std::uint32_t timeout_ms)
    {
        if (buffer == nullptr || buffer_capacity <= 0 || bytes_written == nullptr ||
            timestamp_100ns == nullptr || packet_flags == nullptr || timeout_ms == 0)
        {
            return DDC_E_INVALID_ARGUMENT;
        }

        *bytes_written = 0;
        *timestamp_100ns = 0;
        *packet_flags = 0;
        std::scoped_lock read_lock(read_mutex_);
        if (config_.audio_only != 0 && !audio_start_thread_.joinable())
        {
            audio_start_thread_ = std::thread([this]
            {
                try
                {
                    create_encoder_and_muxer();
                    start_audio_capture();
                }
                catch (...)
                {
                    record_pipeline_failure(std::current_exception());
                }
            });
        }

        media_packet packet;
        {
            std::scoped_lock lock(mutex_);
            if (state_ == state::failed)
            {
                return DDC_E_MEDIA_PIPELINE;
            }

            if (state_ == state::stopped)
            {
                return DDC_END_OF_STREAM;
            }

            if (state_ != state::running)
            {
                set_error_locked("The native media session must be running before packets can be read.");
                return DDC_E_INVALID_STATE;
            }

            if (pending_packet_)
            {
                packet = std::move(*pending_packet_);
                pending_packet_.reset();
            }
        }

        if (packet.bytes.empty())
        {
            switch (output_packets_.pop(packet, std::chrono::milliseconds(timeout_ms)))
            {
            case queue_read_result::timeout:
                return DDC_TIMEOUT;
            case queue_read_result::stopped:
                {
                    std::scoped_lock lock(mutex_);
                    return state_ == state::failed ? DDC_E_MEDIA_PIPELINE : DDC_END_OF_STREAM;
                }
            case queue_read_result::packet:
                break;
            }
        }

        {
            std::scoped_lock lock(mutex_);
            if (state_ != state::running)
            {
                return DDC_END_OF_STREAM;
            }
        }

        if (packet.bytes.size() > static_cast<std::size_t>(buffer_capacity))
        {
            std::scoped_lock lock(mutex_);
            set_error_locked("The managed read buffer is too small for a native MPEG-TS packet chunk.");
            *bytes_written = static_cast<std::int32_t>(packet.bytes.size());
            pending_packet_ = std::move(packet);
            return DDC_E_BUFFER_TOO_SMALL;
        }

        std::memcpy(buffer, packet.bytes.data(), packet.bytes.size());
        *bytes_written = static_cast<std::int32_t>(packet.bytes.size());
        *timestamp_100ns = packet.timestamp_100ns;
        *packet_flags = packet.flags;
        return DDC_OK;
    }

    std::int32_t media_session::get_statistics(ddc_session_statistics* statistics)
    {
        if (statistics == nullptr ||
            statistics->struct_size < static_cast<std::int32_t>(sizeof(ddc_session_statistics)) ||
            statistics->abi_version != DDC_ABI_VERSION)
        {
            return DDC_E_INVALID_ARGUMENT;
        }

        std::scoped_lock lock(mutex_);
        const auto queue_statistics = output_packets_.statistics();
        statistics_.queue_overflows = queue_statistics.overflows >
                static_cast<std::uint64_t>(std::numeric_limits<std::int64_t>::max())
            ? std::numeric_limits<std::int64_t>::max()
            : static_cast<std::int64_t>(queue_statistics.overflows);
        *statistics = statistics_;
        return DDC_OK;
    }

    std::int32_t media_session::copy_encoder_diagnostics(
        ddc_encoder_diagnostics* diagnostics,
        std::uint8_t* encoder_name,
        const std::int32_t encoder_name_capacity,
        std::int32_t* encoder_name_bytes_written)
    {
        if (diagnostics == nullptr ||
            diagnostics->struct_size < static_cast<std::int32_t>(sizeof(ddc_encoder_diagnostics)) ||
            diagnostics->abi_version != DDC_ABI_VERSION ||
            encoder_name_bytes_written == nullptr || encoder_name_capacity < 0 ||
            (encoder_name_capacity > 0 && encoder_name == nullptr))
        {
            return DDC_E_INVALID_ARGUMENT;
        }

        *encoder_name_bytes_written = 0;
        std::scoped_lock lock(mutex_);
        if (state_ == state::failed)
        {
            return DDC_E_MEDIA_PIPELINE;
        }

        if (!encoder_diagnostics_)
        {
            set_error_locked("Encoder diagnostics are not available before the video encoder is initialized.");
            return DDC_E_INVALID_STATE;
        }

        if (encoder_name_.size() >
            static_cast<std::size_t>(std::numeric_limits<std::int32_t>::max()))
        {
            return DDC_E_INTERNAL;
        }

        const auto required = static_cast<std::int32_t>(encoder_name_.size());
        *encoder_name_bytes_written = required;
        if (required > encoder_name_capacity)
        {
            return DDC_E_BUFFER_TOO_SMALL;
        }

        *diagnostics = *encoder_diagnostics_;
        if (required > 0)
        {
            std::memcpy(encoder_name, encoder_name_.data(), static_cast<std::size_t>(required));
        }

        return DDC_OK;
    }

    std::int32_t media_session::stop()
    {
        std::scoped_lock operation_lock(operation_mutex_);
        {
            std::scoped_lock lock(mutex_);
            if (state_ == state::stopped)
            {
                return DDC_OK;
            }

            state_ = state::stopping;
            pending_packet_.reset();
        }

        cleanup_pipeline();
        output_packets_.stop();
        {
            std::scoped_lock lock(mutex_);
            state_ = state::stopped;
        }

        return DDC_OK;
    }

    void media_session::cleanup_pipeline() noexcept
    {
        if (audio_start_thread_.joinable())
        {
            audio_start_thread_.join();
        }

        if (capture_)
        {
            capture_->stop();
        }

        if (audio_capture_)
        {
            audio_capture_->stop();
        }

        if (video_encoder_)
        {
            try
            {
                video_encoder_->drain();
            }
            catch (...)
            {
                record_pipeline_failure(std::current_exception());
            }
        }

        if (audio_encoder_)
        {
            try
            {
                audio_encoder_->drain();
            }
            catch (...)
            {
                record_pipeline_failure(std::current_exception());
            }
        }

        if (mp3_encoder_)
        {
            try
            {
                mp3_encoder_->drain();
            }
            catch (...)
            {
                record_pipeline_failure(std::current_exception());
            }
        }

        if (muxer_)
        {
            try
            {
                muxer_->finish();
            }
            catch (...)
            {
                record_pipeline_failure(std::current_exception());
            }
        }

        capture_.reset();
        audio_capture_.reset();
        adts_writer_.reset();
        audio_encoder_.reset();
        mp3_encoder_.reset();
        video_encoder_.reset();
        muxer_.reset();
        video_processor_.reset();
        software_video_processor_.reset();
    }

    std::int32_t media_session::copy_last_error(
        std::uint8_t* buffer,
        const std::int32_t buffer_capacity,
        std::int32_t* bytes_written)
    {
        if (bytes_written == nullptr || buffer_capacity < 0 ||
            (buffer_capacity > 0 && buffer == nullptr))
        {
            return DDC_E_INVALID_ARGUMENT;
        }

        std::scoped_lock lock(mutex_);
        if (last_error_.size() > static_cast<std::size_t>(std::numeric_limits<std::int32_t>::max()))
        {
            return DDC_E_INTERNAL;
        }

        const auto required = static_cast<std::int32_t>(last_error_.size());
        *bytes_written = required;
        if (required > buffer_capacity)
        {
            return DDC_E_BUFFER_TOO_SMALL;
        }

        if (required > 0)
        {
            std::memcpy(buffer, last_error_.data(), static_cast<std::size_t>(required));
        }

        return DDC_OK;
    }

    void media_session::set_error_locked(std::string message)
    {
        constexpr std::size_t maximum_error_bytes = 4095;
        if (message.size() > maximum_error_bytes)
        {
            message.resize(maximum_error_bytes);
        }

        last_error_ = std::move(message);
    }

    std::int32_t validate_config(const ddc_stream_config* config) noexcept
    {
        if (config == nullptr ||
            config->struct_size < static_cast<std::int32_t>(sizeof(ddc_stream_config)) ||
            config->abi_version != DDC_ABI_VERSION ||
            (config->source_kind != DDC_CAPTURE_SOURCE_DISPLAY &&
             config->source_kind != DDC_CAPTURE_SOURCE_WINDOW) ||
            (config->audio_only == 0 && config->source_handle == 0) ||
            config->width < 2 || config->width > 7680 || (config->width & 1) != 0 ||
            config->height < 2 || config->height > 4320 || (config->height & 1) != 0 ||
            config->frame_rate < 1 || config->frame_rate > 30 ||
            config->video_bitrate < 100'000 || config->video_bitrate > 100'000'000 ||
            config->gop_frames < 1 || config->gop_frames > 300 ||
            config->include_cursor < 0 || config->include_cursor > 1 ||
            config->include_audio < 0 || config->include_audio > 1 ||
            config->mute_local_playback < 0 || config->mute_local_playback > 1 ||
            config->audio_only < 0 || config->audio_only > 1 ||
            config->audio_profile < DDC_AUDIO_PROFILE_NONE ||
            config->audio_profile > DDC_AUDIO_PROFILE_AAC_MPEG_TS ||
            (config->audio_only != 0 && config->include_audio == 0) ||
            (config->audio_only != 0 &&
             config->audio_profile != DDC_AUDIO_PROFILE_AAC_ADTS &&
             config->audio_profile != DDC_AUDIO_PROFILE_MP3 &&
             config->audio_profile != DDC_AUDIO_PROFILE_LPCM &&
             config->audio_profile != DDC_AUDIO_PROFILE_AAC_MPEG_TS) ||
            (config->audio_only == 0 && config->audio_profile != DDC_AUDIO_PROFILE_NONE) ||
            (config->mute_local_playback != 0 && config->include_audio == 0) ||
            config->audio_bitrate < 32'000 || config->audio_bitrate > 512'000 ||
            config->stream_mode != mpeg_ts_continuous_mode)
        {
            return DDC_E_INVALID_ARGUMENT;
        }

        return DDC_OK;
    }
}
