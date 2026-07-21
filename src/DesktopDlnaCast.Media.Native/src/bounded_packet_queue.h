#pragma once

#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <mutex>
#include <vector>

namespace ddc
{
    struct media_packet final
    {
        std::vector<std::uint8_t> bytes;
        std::int64_t timestamp_100ns{};
        std::int32_t flags{};
    };

    enum class queue_read_result
    {
        packet,
        timeout,
        stopped,
    };

    struct packet_queue_statistics final
    {
        std::size_t queued_packets{};
        std::size_t queued_bytes{};
        std::uint64_t overflows{};
    };

    class bounded_packet_queue final
    {
    public:
        bounded_packet_queue(std::size_t maximum_packets, std::size_t maximum_bytes);
        bounded_packet_queue(const bounded_packet_queue&) = delete;
        bounded_packet_queue& operator=(const bounded_packet_queue&) = delete;
        ~bounded_packet_queue() = default;

        [[nodiscard]] bool push(media_packet packet);
        [[nodiscard]] queue_read_result pop(
            media_packet& packet,
            std::chrono::milliseconds timeout);
        void stop() noexcept;
        [[nodiscard]] packet_queue_statistics statistics() const noexcept;

    private:
        const std::size_t maximum_packets_;
        const std::size_t maximum_bytes_;
        mutable std::mutex mutex_;
        std::condition_variable available_;
        std::deque<media_packet> packets_;
        std::size_t queued_bytes_{};
        std::uint64_t overflows_{};
        bool stopped_{};
    };
}
