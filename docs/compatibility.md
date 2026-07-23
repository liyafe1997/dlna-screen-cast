# Compatibility records

Compatibility behavior belongs in data-driven renderer profiles. Brand/model conditionals must not be scattered through protocol or UI code.

## Automated baseline

| Receiver | Version/platform | Mode | Result | Evidence |
| --- | --- | --- | --- | --- |
| Repository MockRenderer | Current source / .NET 10 / Windows | Static MPEG-TS GET | Pass | SSDP discovery, `GetProtocolInfo`, DIDL-Lite, Set URI, Play, media pull/validation, transport polling, Stop |
| Repository MockRenderer | Current source / .NET 10 / Windows | Static MPEG-TS HEAD | Pass | Delayed HEAD, custom header, sensitive-header redaction, PLAYING transition |
| Repository MockRenderer | Current source / .NET 10 / Windows | Failure injection | Pass | Metadata rejection with one empty retry, SOAP Fault, midstream disconnect, deterministic cleanup |
| Repository MockRenderer | Current source / .NET 10 / Windows | Continuous MPEG-TS endpoint | Pass | Keyframe-aware startup snapshot, chunked live response, repeated PAT/PMT/H.264/AAC asset, bounded receiver read |
| Repository MockRenderer | Current source / .NET 10 / Windows 11 ARM64 host, translated x64 target | 720p30 WGC continuous MPEG-TS, no GPU video interfaces | Pass | `libswscale` pixel conversion, Microsoft software H.264 MFT, 120-second pull, 85,616 validated TS packets, zero media-validation failures, clean Stop |
| Repository MockRenderer | Current source / .NET 10 / Windows 11 ARM64 host, translated x64 target | Repeated software-fallback lifecycle | Pass | 20/20 `Start -> Play -> HTTP GET -> Stop` iterations in one process, 20 first-media-byte events, final session `Idle`, renderer `STOPPED` |
| Repository MockRenderer | Current source / .NET 10 / Windows 11 ARM64 host, native arm64 target | 720p30 WGC continuous MPEG-TS with AAC, no GPU video interfaces | Pass | Fully native ARM64 binaries (app, `DesktopDlnaCast.Media.Native.dll`, FFmpeg, CRT); `libswscale` pixel conversion, Microsoft software H.264 MFT, MF AAC-LC; 10-second pull, 2,247,540 bytes / 11,955 validated TS packets, zero media-validation failures, session `Idle`, renderer `STOPPED` |
| Repository MockRenderer | Current source / .NET 10 / Windows x64 target | Audio-only continuous MPEG-TS | Pass | WASAPI + Microsoft AAC-LC only; no WGC/H.264 requirement; 2-second pull, 49,256 bytes / 262 validated TS packets, AAC present, one renderer GET, clean `Idle`/`STOPPED` cleanup |

The checked-in `test-pattern.ts` is 289,332 bytes and 2.026667 seconds long. Development-time `ffprobe` inspection reported MPEG-TS containing H.264 Main, 640x360 at 30 fps, plus AAC-LC stereo at 48 kHz. Repository tests independently require TS packet sync, PAT, PMT, H.264 stream type, and AAC stream type, so CI does not require FFmpeg.

StreamProbe's strict parser reports 1,539 TS packets, 60 video PTS observations, 95 audio PTS observations, two IDRs, SPS/PPS, monotonic PTS, and a maximum IDR interval of 90,000 ticks (one second at the MPEG clock). A process-level integration test runs the CLI against the Kestrel endpoint and verifies that its JSON does not reveal the session token.

MockRenderer defaults to loopback, dynamic HTTP/SSDP ports, a fixed test UDN, and a 4 MiB media read ceiling. It never requires a physical TV, cloud service, or public network.

## External smoke tests

Kodi on Windows was discovered on 2026-07-23 as a MediaRenderer (`UPnP/1.0 DLNADOC/1.50 Kodi`). The earlier AAC-only MPEG-TS attempt failed before Kodi requested HTTP because an audio DIDL item exposed a `video/mpeg` resource. The implementation now uses a matching native audio resource and `object.item.audioItem.musicTrack`; a post-change interactive Kodi playback run remains to be completed, so no successful Kodi compatibility claim is made yet. Kodi remains an optional third-party check and does not replace MockRenderer.

The repository MockRenderer and NativeCastProbe completed a five-second production pure-audio run on 2026-07-23 using the Microsoft Media Foundation MP3 encoder: one HTTP request, first media byte observed, 81,408 bytes validated, no validation failure, and deterministic Stop/cleanup.

The x64 target now has a successful real desktop capture result on the ARM64 development host through Windows x64 translation. That host exposes ordinary WGC/D3D11 capture textures but neither hardware nor WARP D3D11 video-processor interfaces, and its hardware H.264 request returns `Function not implemented`. The production selector therefore chose `libswscale` for BGRA-to-NV12 and the inbox Microsoft software H.264 MFT. A 10-second `NativeCaptureProbe` produced 289 video PTS observations and 10 IDRs; StreamProbe confirmed PAT/PMT, H.264, SPS/PPS, monotonic PTS, and a maximum IDR interval of about one second. The sensitive capture file was deleted after validation.

The no-disk `NativeCastProbe` then passed both a 120-second 720p30 pull and 20 consecutive two-second lifecycle iterations. The long run delivered 16,095,808 bytes and 85,616 validated TS packets with no validation failure. These results validate the software fallback and x64 translation flow; they do not claim hardware-encoder coverage.

On 2026-07-22, the native ARM64 target completed its first end-to-end acceptance on the same ARM64 development host, with every binary in the pipeline (probe, managed stack, `DesktopDlnaCast.Media.Native.dll`, vcpkg-built FFmpeg, app-local CRT) built for ARM64 and running without translation. The native CTest component suite passed, and a 10-second 720p30 `NativeCastProbe` run with AAC validated 2,247,540 bytes / 11,955 TS packets with zero failures, using `libswscale` conversion and the Microsoft software H.264 MFT (this VM registers no hardware H.264 MFT and no D3D11 video device). Hardware-encoder (e.g. Snapdragon) and Adreno D3D11 video-processor coverage on physical ARM64 hardware remains a release-candidate manual check.

An additional 10-second ABI v3 capture enabled system audio at 128 kbit/s. It produced 1,942,228 bytes with 287 video PTS values, 461 audio PTS values, 10 IDRs, zero dropped video frames, and zero queue overflows. StreamProbe confirmed PAT/PMT, monotonic PTS, H.264 and AAC; optional `ffprobe` identified H.264 Main 1280x720 30 fps plus AAC-LC 48 kHz stereo. A separate five-second 1920x1080/6 Mbit/s run passed the same strict parser with 95 video PTS values, 219 audio PTS values, five time-based IDRs, and a maximum IDR interval of 104,861 ticks; `ffprobe` reported an average 30 fps.

On 2026-07-20, an ABI v4 `NativeCastProbe` run on Windows 11 build 26200 enabled `--mute-local-playback true` for two seconds against the repository MockRenderer. The endpoint-control path initialized successfully, the receiver completed one GET, validated 682,252 bytes / 3,629 MPEG-TS packets with H.264/AAC, and both the cast session and renderer returned to `Idle` / `STOPPED`. This virtual-machine run did not provide an acoustic observation of a physical speaker, so physical silence and pre-volume signal amplitude remain a release-candidate manual check.

The no-disk MockRenderer A/V stability run then completed 120 seconds at 720p30 with AAC enabled. It validated 35,805,352 bytes and 190,454 MPEG-TS packets, reported no media validation failure, and returned both the application session and Renderer to `Idle`/`STOPPED`. The host was silent, so this verifies continuous timestamped AAC silence, bounded transport, pull lifetime, and cleanup; perceptual synchronization with audible content and a live default-output-device switch remain manual/outstanding checks.

Future records should include receiver name/version/platform, fault configuration, `GetProtocolInfo`, selected profile, startup time, approximate latency, audio behavior, and quirks. Physical-device records additionally include brand, model, and firmware.
