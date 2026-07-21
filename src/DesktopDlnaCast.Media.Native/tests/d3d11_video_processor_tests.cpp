#include "d3d11_video_processor.h"
#include "test_assert.h"

void run_d3d11_video_processor_tests()
{
    const RECT exact = ddc::calculate_letterbox_rect(1920, 1080, 1280, 720);
    DDC_TEST_CHECK(exact.left == 0);
    DDC_TEST_CHECK(exact.top == 0);
    DDC_TEST_CHECK(exact.right == 1280);
    DDC_TEST_CHECK(exact.bottom == 720);

    const RECT pillarbox = ddc::calculate_letterbox_rect(1024, 768, 1280, 720);
    DDC_TEST_CHECK(pillarbox.left == 160);
    DDC_TEST_CHECK(pillarbox.top == 0);
    DDC_TEST_CHECK(pillarbox.right == 1120);
    DDC_TEST_CHECK(pillarbox.bottom == 720);

    const RECT letterbox = ddc::calculate_letterbox_rect(2560, 1080, 1280, 720);
    DDC_TEST_CHECK(letterbox.left == 0);
    DDC_TEST_CHECK(letterbox.top == 90);
    DDC_TEST_CHECK(letterbox.right == 1280);
    DDC_TEST_CHECK(letterbox.bottom == 630);
}
