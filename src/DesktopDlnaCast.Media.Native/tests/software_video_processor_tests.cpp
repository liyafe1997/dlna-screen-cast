#include "software_video_processor.h"
#include "test_assert.h"

#include <d3d11.h>
#include <winrt/base.h>

#include <algorithm>
#include <cstdint>
#include <ranges>
#include <vector>

namespace
{
    [[nodiscard]] winrt::com_ptr<ID3D11Device> create_warp_device()
    {
        winrt::com_ptr<ID3D11Device> device;
        D3D_FEATURE_LEVEL feature_level{};
        const HRESULT result = D3D11CreateDevice(
            nullptr,
            D3D_DRIVER_TYPE_WARP,
            nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr,
            0,
            D3D11_SDK_VERSION,
            device.put(),
            &feature_level,
            nullptr);
        DDC_TEST_CHECK(SUCCEEDED(result));
        return device;
    }

    [[nodiscard]] winrt::com_ptr<ID3D11Texture2D> create_white_bgra_texture(
        const winrt::com_ptr<ID3D11Device>& device,
        const std::uint32_t width,
        const std::uint32_t height)
    {
        std::vector<std::uint8_t> pixels(
            static_cast<std::size_t>(width) * height * 4U,
            255);
        D3D11_TEXTURE2D_DESC description{};
        description.Width = width;
        description.Height = height;
        description.MipLevels = 1;
        description.ArraySize = 1;
        description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        description.SampleDesc.Count = 1;
        description.Usage = D3D11_USAGE_DEFAULT;
        D3D11_SUBRESOURCE_DATA initial_data{};
        initial_data.pSysMem = pixels.data();
        initial_data.SysMemPitch = width * 4U;
        initial_data.SysMemSlicePitch = static_cast<UINT>(pixels.size());
        winrt::com_ptr<ID3D11Texture2D> texture;
        DDC_TEST_CHECK(SUCCEEDED(device->CreateTexture2D(
            &description,
            &initial_data,
            texture.put())));
        return texture;
    }
}

void run_software_video_processor_tests()
{
    constexpr std::int32_t output_width = 8;
    constexpr std::int32_t output_height = 8;
    const auto device = create_warp_device();
    ddc::software_video_processor processor(
        output_width, output_height, DDC_ASPECT_RATIO_LETTERBOX);

    const auto wide_texture = create_white_bgra_texture(device, 4, 2);
    const auto letterboxed = processor.process(wide_texture, 4, 2);
    DDC_TEST_CHECK(letterboxed.size() == 96U);
    DDC_TEST_CHECK(std::ranges::all_of(
        letterboxed | std::views::take(16),
        [](const std::uint8_t value) { return value == 16; }));
    DDC_TEST_CHECK(letterboxed[2U * output_width] > 200);
    DDC_TEST_CHECK(letterboxed[5U * output_width] > 200);
    DDC_TEST_CHECK(std::ranges::all_of(
        letterboxed | std::views::drop(6U * output_width) |
            std::views::take(2U * output_width),
        [](const std::uint8_t value) { return value == 16; }));

    // Reusing the processor with a different source size must recreate both
    // the staging texture and the swscale context without retaining stale strides.
    const auto tall_texture = create_white_bgra_texture(device, 2, 4);
    const auto pillarboxed = processor.process(tall_texture, 2, 4);
    DDC_TEST_CHECK(pillarboxed.size() == 96U);
    DDC_TEST_CHECK(pillarboxed[0] == 16);
    DDC_TEST_CHECK(pillarboxed[2] > 200);
    DDC_TEST_CHECK(pillarboxed[5] > 200);
    DDC_TEST_CHECK(pillarboxed[6] == 16);
}
