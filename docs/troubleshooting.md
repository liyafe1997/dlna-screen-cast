# Troubleshooting

## Build fails before compilation

Run `dotnet --info` and confirm that the SDK selected by `global.json` is installed. The WinUI project also needs a current Windows SDK and Windows App SDK-compatible Visual Studio toolchain. Use the exact restore/build/test commands in the repository README.

The Visual Studio solution builds the managed projects; the Milestone 2 DLL is a separate CMake target generated through the repository preset rather than a checked-in `.vcxproj`. Building it requires MSVC x64 C++ tools, Windows 11 SDK headers/libraries, C++/WinRT, and the pinned vcpkg FFmpeg development package containing `libavcodec`, `libavformat`, `libavutil`, and `libswscale` headers/import libraries. An FFmpeg executable alone is insufficient.

## Casting fails with `DllNotFoundException` or `0x8007007E`

This happens before capture starts and is unrelated to SSDP, SOAP, Kodi, or the selected renderer. Build the native preset before launching the WinUI application:

```powershell
$env:VCPKG_ROOT = "C:\path\to\vcpkg"
cmake --preset native-x64-release
cmake --build --preset native-x64-release
dotnet build src/DesktopDlnaCast.App/DesktopDlnaCast.App.csproj -c Debug
```

The application output must contain all five files: `DesktopDlnaCast.Media.Native.dll`, `avcodec-*.dll`, `avformat-*.dll`, `avutil-*.dll`, and `swscale-*.dll`. MSBuild recognizes both the documented multi-config preset output and the repository single-config development output, copies all five files, and rejects an incomplete native-consuming application or probe during build. If custom CMake output directories are used, pass `NativeMediaOutputDirectory` and `NativeMediaDependencyDirectory` as MSBuild properties.

## Capture starts on a VM but GPU processing is unavailable

The pipeline first tries the capture device's D3D11 Video Processor. If `ID3D11VideoDevice`/`ID3D11VideoContext` or BGRA-to-NV12 support is unavailable, it selects `Libswscale` once for the remainder of the session. H.264 selection is independent: `h264_mf` first forces a hardware MFT and, if that open fails, opens the inbox Microsoft software MFT. Encoder diagnostics report both `IsHardware` and `VideoProcessorBackend`; the diagnostic name includes bounded fallback context. This mode uses more CPU, so start with 1280x720 at 30 fps and the compatibility bitrate.

## Application does not start

The application is unpackaged. Build the x64 target on Windows 11 and launch from its output directory so `appsettings.json` is available. Review the debugger or console structured log for host startup and options-validation errors.

## No renderer is discovered

- Confirm the PC and renderer are on the same reachable LAN and the Windows network profile is Private.
- Check that Wi-Fi client isolation is disabled and multicast UDP is allowed.
- VPN, TUN/TAP, tunnel, disconnected, and link-local-only interfaces are excluded by default. Hyper-V, VMware, and VirtualBox guest adapters remain eligible when they expose a usable IPv4 LAN address.
- Device Description and SOAP responses have strict size and timeout limits; malformed device data is intentionally rejected.
- MockRenderer uses a dynamically assigned SSDP test port by default. Production discovery uses multicast port 1900, so direct CLI testing of the GUI requires starting MockRenderer on an appropriate non-loopback interface with `--allow-non-loopback --ssdp-port 1900` and must not conflict with another listener.

## Renderer is found but does not request the stream

- Check the selected route-derived local IPv4 in structured logs; the URL must not contain localhost, wildcard, hostname, or an unrelated VPN address.
- Ensure Windows Firewall allows inbound TCP for the application on the Private profile. The application never disables the firewall or creates NAT/IGD mappings.
- Check the exact AVTransport service version, SOAP HTTP status/Fault, selected MIME type, and whether the renderer rejected DIDL-Lite metadata.
- Metadata UPnP error 714 is retried once with empty metadata; repeated failures stop the session.

## Renderer requests the URL but playback does not start

Verify HTTP status and `Content-Type: video/mpeg`. The built-in clip contains H.264/AAC, PAT, and PMT. Compare the receiver's advertised sink protocol info, but remember that many devices under-report formats. Use MockRenderer `/test/events` to inspect action order, first-byte timing, media validation, and connection completion reason.

## Stop or cleanup problems

Renderer `Stop` is best effort. Local shutdown must still invalidate the token and close the Kestrel listener. A stopped URL should fail to connect; continuing access indicates a cleanup defect.

## The PC stays muted after casting

The optional local-playback mute affects every sound on the Windows output endpoint being captured. Normal Stop, cancellation, startup failure, application shutdown, and default-device changes restore an app-owned mute. If the user changes the endpoint mute state while casting, DesktopDlnaCast leaves that newer user state alone. A process crash or forced termination can prevent cleanup; use the Windows volume control to unmute the endpoint, then restart the application before casting again. `LocalPlaybackMuteChanges` and `LocalPlaybackMuteRestoreFailures` in native statistics help diagnose cleanup failures. The feature relies on pre-volume WASAPI loopback behavior and therefore requires physical validation on the target Windows/audio-driver combination.

## Firewall rule removal

Milestone 1 does not create or modify firewall rules. If a rule was added manually, remove it in **Windows Security → Firewall & network protection → Advanced settings → Inbound Rules**, selecting only the rule created for DesktopDlnaCast. Do not disable Windows Firewall globally.
