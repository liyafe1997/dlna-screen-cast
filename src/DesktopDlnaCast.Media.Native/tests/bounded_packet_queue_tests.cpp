#include "bounded_packet_queue.h"
#include "test_assert.h"

#include <chrono>
#include <cstdint>
#include <vector>

void run_bounded_packet_queue_tests()
{
    using namespace std::chrono_literals;
    ddc::bounded_packet_queue queue(2, 8);
    DDC_TEST_CHECK(queue.push({ std::vector<std::uint8_t>(4, 1), 10, 0 }));
    DDC_TEST_CHECK(queue.push({ std::vector<std::uint8_t>(4, 2), 20, 0 }));
    DDC_TEST_CHECK(queue.push({ std::vector<std::uint8_t>(4, 3), 30, 0 }));

    const auto after_overflow = queue.statistics();
    DDC_TEST_CHECK(after_overflow.queued_packets == 2);
    DDC_TEST_CHECK(after_overflow.queued_bytes == 8);
    DDC_TEST_CHECK(after_overflow.overflows == 1);

    ddc::media_packet packet;
    DDC_TEST_CHECK(queue.pop(packet, 1ms) == ddc::queue_read_result::packet);
    DDC_TEST_CHECK(packet.timestamp_100ns == 20);
    DDC_TEST_CHECK(packet.bytes.front() == 2);
    DDC_TEST_CHECK(queue.pop(packet, 1ms) == ddc::queue_read_result::packet);
    DDC_TEST_CHECK(packet.timestamp_100ns == 30);
    DDC_TEST_CHECK(queue.pop(packet, 1ms) == ddc::queue_read_result::timeout);

    queue.stop();
    queue.stop();
    DDC_TEST_CHECK(!queue.push({ std::vector<std::uint8_t>(4, 4), 40, 0 }));
    DDC_TEST_CHECK(queue.pop(packet, 1ms) == ddc::queue_read_result::stopped);

    ddc::bounded_packet_queue stopped_with_data(2, 8);
    DDC_TEST_CHECK(stopped_with_data.push({ std::vector<std::uint8_t>(4, 5), 50, 0 }));
    stopped_with_data.stop();
    DDC_TEST_CHECK(stopped_with_data.statistics().queued_packets == 0);
    DDC_TEST_CHECK(stopped_with_data.pop(packet, 1ms) == ddc::queue_read_result::stopped);
}
