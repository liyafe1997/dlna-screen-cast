#include "d3d11_video_processor.h"
#include "test_assert.h"

void run_d3d11_video_processor_tests()
{
    const auto exact_layout = ddc::calculate_video_layout(
        1920, 1080, 1280, 720, DDC_ASPECT_RATIO_LETTERBOX);
    const RECT exact = exact_layout.destination;
    DDC_TEST_CHECK(exact.left == 0);
    DDC_TEST_CHECK(exact.top == 0);
    DDC_TEST_CHECK(exact.right == 1280);
    DDC_TEST_CHECK(exact.bottom == 720);

    const auto pillarbox_layout = ddc::calculate_video_layout(
        1024, 768, 1280, 720, DDC_ASPECT_RATIO_LETTERBOX);
    const RECT pillarbox = pillarbox_layout.destination;
    DDC_TEST_CHECK(pillarbox.left == 160);
    DDC_TEST_CHECK(pillarbox.top == 0);
    DDC_TEST_CHECK(pillarbox.right == 1120);
    DDC_TEST_CHECK(pillarbox.bottom == 720);

    const auto letterbox_layout = ddc::calculate_video_layout(
        2560, 1080, 1280, 720, DDC_ASPECT_RATIO_LETTERBOX);
    const RECT letterbox = letterbox_layout.destination;
    DDC_TEST_CHECK(letterbox.left == 0);
    DDC_TEST_CHECK(letterbox.top == 90);
    DDC_TEST_CHECK(letterbox.right == 1280);
    DDC_TEST_CHECK(letterbox.bottom == 630);

    const auto stretch = ddc::calculate_video_layout(
        1920, 1200, 1280, 720, DDC_ASPECT_RATIO_STRETCH);
    DDC_TEST_CHECK(stretch.source.left == 0);
    DDC_TEST_CHECK(stretch.source.bottom == 1200);
    DDC_TEST_CHECK(stretch.destination.right == 1280);
    DDC_TEST_CHECK(stretch.destination.bottom == 720);

    const auto crop = ddc::calculate_video_layout(
        1920, 1200, 1280, 720, DDC_ASPECT_RATIO_CENTER_CROP);
    DDC_TEST_CHECK(crop.source.left == 0);
    DDC_TEST_CHECK(crop.source.top == 60);
    DDC_TEST_CHECK(crop.source.right == 1920);
    DDC_TEST_CHECK(crop.source.bottom == 1140);
    DDC_TEST_CHECK(crop.destination.right == 1280);
    DDC_TEST_CHECK(crop.destination.bottom == 720);
}
