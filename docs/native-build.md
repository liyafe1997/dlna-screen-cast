# Native media build

## Dependency decision

The native media core uses Windows platform APIs first, with bounded software fallbacks:

- Windows.Graphics.Capture through C++/WinRT
- D3D11 Video Processor for preferred GPU scaling and BGRA-to-NV12 conversion
- FFmpeg `libswscale` for the same conversion when the display driver exposes ordinary D3D11 capture textures but no video-processor interfaces
- Media Foundation H.264 (and later AAC) encoders, selected through FFmpeg's `h264_mf` wrapper so asynchronous hardware MFTs and the synchronous Windows software MFT share one bounded interface
- FFmpeg `libavformat` for MPEG-TS/HLS muxing

The Windows SDK and Media Foundation do not provide the required MPEG-TS/HLS live muxing control, so `libavformat` is necessary. D3D11 WARP and reference devices explicitly do not implement `ID3D11VideoDevice`; therefore the D3D11 Video Processor cannot be the only pixel path in a no-GPU VM. `libswscale` is the focused fallback for scaling and pixel conversion and does not replace WGC capture. The H.264 software fallback remains the inbox Microsoft Media Foundation encoder; DesktopDlnaCast does not add a third-party H.264 implementation.

The repository uses vcpkg manifest mode because it provides a per-project dependency tree and a pinned registry baseline. The manifest disables FFmpeg default features and requests only `avformat` and `swscale`; `avcodec` supplies the `h264_mf` Media Foundation wrapper. GPL, nonfree, x264, and x265 features are not enabled.

The pinned vcpkg baseline is `0878b5224d4a4968940ee296a2e7fae2d3b62983`, where the official FFmpeg port reports version `8.1.2#3`. Review and deliberately update this hash rather than floating on the registry head.

## License policy

FFmpeg's official legal page states that its base license is LGPL 2.1-or-later, while optional GPL components make the entire FFmpeg build GPL. DesktopDlnaCast therefore:

- builds without GPL and nonfree features;
- dynamically links the FFmpeg DLLs on Windows;
- does not enable libx264/libx265;
- records the exact vcpkg baseline, triplet, and feature set;
- must distribute or link to the exact corresponding FFmpeg source archive and build instructions with any application binaries;
- must display the FFmpeg/LGPL notice in the future About UI and release documentation.

This is a project engineering policy, not legal advice. Release packaging remains blocked until the source-correspondence and notice workflow is implemented and reviewed.

Primary references:

- <https://ffmpeg.org/legal.html>
- <https://learn.microsoft.com/vcpkg/concepts/manifest-mode>
- <https://learn.microsoft.com/windows/apps/develop/cpp-winrt/get-started>
- <https://learn.microsoft.com/windows/win32/medfound/h-264-video-encoder>
- <https://learn.microsoft.com/windows/win32/api/d3d11/nn-d3d11-id3d11videodevice>
- <https://ffmpeg.org/ffmpeg-scaler.html>
- <https://github.com/microsoft/vcpkg/tree/master/ports/ffmpeg>

## Prerequisites

- Visual Studio 2022 Build Tools or Visual Studio with MSVC x64 C++ tools
  (add the "MSVC ... ARM64/ARM64EC build tools" component for the arm64 target)
- Windows 11 SDK 10.0.26100 or a later supported SDK (x64 and/or arm64 libs
  matching the targets you build)
- CMake 3.28 or later
- Git
- vcpkg at the pinned baseline

The native x64 Release target is tested with MSVC 14.51, Windows SDK 10.0.26100, CMake 4.3.3, and the pinned vcpkg dependency tree. `/W4 /WX` and CTest cover D3D11 letterboxing, WARP texture readback plus `libswscale` conversion, forced software-MFT H.264 encoding, hardware-preferred encoder selection, and `libavformat` MPEG-TS muxing. The current host is ARM64 and runs the x64 target through Windows translation; runtime probe results are recorded in `docs/compatibility.md` rather than inferred from component tests.

## Configure and build

```powershell
git clone https://github.com/microsoft/vcpkg "$env:USERPROFILE\src\vcpkg"
git -C "$env:USERPROFILE\src\vcpkg" checkout 0878b5224d4a4968940ee296a2e7fae2d3b62983
& "$env:USERPROFILE\src\vcpkg\bootstrap-vcpkg.bat" -disableMetrics
$env:VCPKG_ROOT = "$env:USERPROFILE\src\vcpkg"

cmake --preset native-x64-release
cmake --build --preset native-x64-release
ctest --preset native-x64-release
```

The arm64 target uses the parallel `native-arm64-release` presets with the
`arm64-windows-desktopdlna` overlay triplet (same pinned baseline, features,
and dynamic linkage). Cross-compiling from an x64 host works for configure and
build; `ctest --preset native-arm64-release` must run on an ARM64 machine:

```powershell
cmake --preset native-arm64-release
cmake --build --preset native-arm64-release
```

`vcpkg_installed`, CMake build outputs, and FFmpeg binaries are ignored by Git. When the preset output exists, the WinUI project copies `DesktopDlnaCast.Media.Native.dll` plus the required `avcodec`, `avformat`, `avutil`, and `swscale` DLLs into its build and publish outputs; the architecture is selected by the managed `RuntimeIdentifier` (`win-x64` → `out/native-x64-release`, `win-arm64` → `out/native-arm64-release`). The MSBuild integration also recognizes the repository's single-config development layout (`out/native-local-release` for x64, `out/native-local-arm64-release` for arm64) with dependencies under `out/native-vcpkg-workspace/<arch>-windows-workspace`. The WinUI application and native runtime probes fail early with an actionable message if the native DLL or any of those four FFmpeg DLLs is absent; they no longer defer this failure until the first cast attempt. The managed-only CI job explicitly disables this packaging check because the separate native job owns the CMake build gate. The source tree does not commit or distribute these binaries.

The GitHub Actions `native` job performs the same configure/build/CTest sequence on `windows-latest`. The `native-arm64` job cross-compiles the arm64 target on the same x64 runner as a compilation gate only — it cannot execute arm64 binaries, so arm64 CTest and runtime acceptance stay on an ARM64 machine. Neither job's presence is evidence that the current unpushed worktree has run remotely.

Use [NativeCaptureProbe](../tools/NativeCaptureProbe/README.md) for a bounded capture-to-file check followed by StreamProbe. Use [NativeCastProbe](../tools/NativeCastProbe/README.md) for the no-disk production `WGC → H.264 → MPEG-TS → Kestrel → UPnP → MockRenderer` smoke or two-minute acceptance run.
