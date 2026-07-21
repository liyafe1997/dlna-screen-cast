# Third-party notices

DesktopDlnaCast currently references the following packages. They are restored from NuGet and are not copied into this repository.

| Component | Purpose | License |
| --- | --- | --- |
| Microsoft.WindowsAppSDK | WinUI 3 desktop application framework | Microsoft Software License Terms |
| Microsoft.Extensions.* | Dependency injection, configuration and logging | MIT |
| xUnit.net | Automated tests | Apache-2.0 |
| Microsoft.NET.Test.Sdk | Test host | MIT |

The Windows.Graphics.Capture interop implementation follows API usage patterns from Microsoft's `Windows.UI.Composition-Win32-Samples` ScreenCaptureforHWND sample, which is licensed under the MIT License. No sample binary is distributed.

## Native runtime dependency

FFmpeg 8.1.2 is declared by the native vcpkg manifest and the pinned dynamic x64 build has been verified locally, but its binaries are not committed to or distributed by this repository. Only the `avformat` and `swscale` features plus `avcodec`'s Media Foundation wrapper are selected, with default/GPL/nonfree features disabled and dynamic Windows linkage required.

FFmpeg is licensed under LGPL 2.1-or-later unless GPL components are enabled. Release packaging must include the exact corresponding source, build configuration, license text, and user-facing notice described in [docs/native-build.md](docs/native-build.md). DesktopDlnaCast does not use FFmpeg's libx264/libx265 features.
