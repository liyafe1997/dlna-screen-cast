#include "media_session.h"

#include <new>

namespace
{
    [[nodiscard]] ddc::media_session* as_session(const ddc_session_handle handle) noexcept
    {
        return static_cast<ddc::media_session*>(handle);
    }
}

extern "C"
{
    std::int32_t ddc_session_create(
        const ddc_stream_config* config,
        ddc_session_handle* result)
    {
        if (result == nullptr)
        {
            return DDC_E_INVALID_ARGUMENT;
        }

        *result = nullptr;
        const auto validation = ddc::validate_config(config);
        if (validation != DDC_OK)
        {
            return validation;
        }

        try
        {
            auto* session = new (std::nothrow) ddc::media_session(*config);
            if (session == nullptr)
            {
                return DDC_E_INTERNAL;
            }

            *result = session;
            return DDC_OK;
        }
        catch (...)
        {
            return DDC_E_INTERNAL;
        }
    }

    std::int32_t ddc_session_start(const ddc_session_handle handle)
    {
        auto* session = as_session(handle);
        if (session == nullptr)
        {
            return DDC_E_INVALID_ARGUMENT;
        }

        try
        {
            return session->start();
        }
        catch (...)
        {
            return DDC_E_INTERNAL;
        }
    }

    std::int32_t ddc_session_read(
        const ddc_session_handle handle,
        std::uint8_t* buffer,
        const std::int32_t buffer_capacity,
        std::int32_t* bytes_written,
        std::int64_t* timestamp_100ns,
        std::int32_t* packet_flags,
        const std::uint32_t timeout_ms)
    {
        auto* session = as_session(handle);
        if (session == nullptr)
        {
            return DDC_E_INVALID_ARGUMENT;
        }

        try
        {
            return session->read(
                buffer,
                buffer_capacity,
                bytes_written,
                timestamp_100ns,
                packet_flags,
                timeout_ms);
        }
        catch (...)
        {
            return DDC_E_INTERNAL;
        }
    }

    std::int32_t ddc_session_get_statistics(
        const ddc_session_handle handle,
        ddc_session_statistics* statistics)
    {
        auto* session = as_session(handle);
        if (session == nullptr)
        {
            return DDC_E_INVALID_ARGUMENT;
        }

        try
        {
            return session->get_statistics(statistics);
        }
        catch (...)
        {
            return DDC_E_INTERNAL;
        }
    }

    std::int32_t ddc_session_get_encoder_diagnostics(
        const ddc_session_handle handle,
        ddc_encoder_diagnostics* diagnostics,
        std::uint8_t* encoder_name_utf8,
        const std::int32_t encoder_name_capacity,
        std::int32_t* encoder_name_bytes_written)
    {
        auto* session = as_session(handle);
        if (session == nullptr)
        {
            return DDC_E_INVALID_ARGUMENT;
        }

        try
        {
            return session->copy_encoder_diagnostics(
                diagnostics,
                encoder_name_utf8,
                encoder_name_capacity,
                encoder_name_bytes_written);
        }
        catch (...)
        {
            return DDC_E_INTERNAL;
        }
    }

    std::int32_t ddc_session_stop(const ddc_session_handle handle)
    {
        auto* session = as_session(handle);
        if (session == nullptr)
        {
            return DDC_OK;
        }

        try
        {
            return session->stop();
        }
        catch (...)
        {
            return DDC_E_INTERNAL;
        }
    }

    std::int32_t ddc_session_get_last_error(
        const ddc_session_handle handle,
        std::uint8_t* utf8_buffer,
        const std::int32_t buffer_capacity,
        std::int32_t* bytes_written)
    {
        auto* session = as_session(handle);
        if (session == nullptr)
        {
            return DDC_E_INVALID_ARGUMENT;
        }

        try
        {
            return session->copy_last_error(utf8_buffer, buffer_capacity, bytes_written);
        }
        catch (...)
        {
            return DDC_E_INTERNAL;
        }
    }

    void ddc_session_destroy(const ddc_session_handle handle)
    {
        // Keep allocation and deletion in this DLL so callers never depend on
        // the C++ class layout or a particular C runtime heap.
        delete as_session(handle);
    }
}
