#include "bounded_packet_queue.h"

#include <stdexcept>
#include <utility>

namespace ddc
{
    bounded_packet_queue::bounded_packet_queue(
        const std::size_t maximum_packets,
        const std::size_t maximum_bytes)
        : maximum_packets_(maximum_packets), maximum_bytes_(maximum_bytes)
    {
        if (maximum_packets == 0 || maximum_bytes == 0)
        {
            throw std::invalid_argument("A bounded packet queue requires positive limits.");
        }
    }

    bool bounded_packet_queue::push(media_packet packet)
    {
        if (packet.bytes.empty())
        {
            return false;
        }

        std::unique_lock lock(mutex_);
        if (stopped_)
        {
            return false;
        }

        const auto incoming_bytes = packet.bytes.size();
        if (incoming_bytes > maximum_bytes_)
        {
            ++overflows_;
            return false;
        }

        while (!packets_.empty() &&
               (packets_.size() >= maximum_packets_ ||
                queued_bytes_ > maximum_bytes_ - incoming_bytes))
        {
            queued_bytes_ -= packets_.front().bytes.size();
            packets_.pop_front();
            ++overflows_;
        }

        queued_bytes_ += incoming_bytes;
        packets_.push_back(std::move(packet));
        lock.unlock();
        available_.notify_one();
        return true;
    }

    queue_read_result bounded_packet_queue::pop(
        media_packet& packet,
        const std::chrono::milliseconds timeout)
    {
        if (timeout <= std::chrono::milliseconds::zero())
        {
            return queue_read_result::timeout;
        }

        std::unique_lock lock(mutex_);
        if (!available_.wait_for(lock, timeout, [this] { return stopped_ || !packets_.empty(); }))
        {
            return queue_read_result::timeout;
        }

        if (packets_.empty())
        {
            return queue_read_result::stopped;
        }

        queued_bytes_ -= packets_.front().bytes.size();
        packet = std::move(packets_.front());
        packets_.pop_front();
        return queue_read_result::packet;
    }

    void bounded_packet_queue::stop() noexcept
    {
        {
            std::scoped_lock lock(mutex_);
            stopped_ = true;
            packets_.clear();
            queued_bytes_ = 0;
        }

        available_.notify_all();
    }

    packet_queue_statistics bounded_packet_queue::statistics() const noexcept
    {
        std::scoped_lock lock(mutex_);
        return { packets_.size(), queued_bytes_, overflows_ };
    }
}
