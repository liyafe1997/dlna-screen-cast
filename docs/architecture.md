# Architecture

## Product boundary

DesktopDlnaCast publishes media over HTTP and controls a UPnP/DLNA Digital Media Renderer (DMR) to play it. The renderer pulls the stream. The application is not a Miracast peer, Windows display driver, remote-desktop endpoint, or DLNA MediaServer.

## Layering

```text
DesktopDlnaCast.App (WinUI 3 + MVVM)
              |
              v
DesktopDlnaCast.Core (use cases, state, contracts, errors)
       /              |                    \
      v               v                     v
    Upnp           Streaming           Media.Interop
 SSDP/SOAP       Kestrel/HTTP        versioned C ABI
                                             |
                                             v
                                  Media.Native (C++/WinRT)
```

Dependencies point inward toward `Core`. `App` composes the process with `Microsoft.Extensions.DependencyInjection`. Views contain no SSDP, SOAP, HTTP streaming, capture, or encoding implementation.

## Application lifetime

The WinUI `App` owns a generic host. Launch starts the host and resolves one main window; window close stops and disposes the host with a bounded timeout. Long-running work is owned by cancellable services. MVP permits exactly one cast/test session.

`StaticMediaTestSession` owns the Milestone 1 operation and drives the shared state machine:

```text
Idle -> ProbingRenderer -> StartingMediaPipeline -> WaitingForKeyframe
     -> Publishing -> SendingTransportUri -> StartingPlayback -> Playing
     -> Stopping -> Idle
```

An active state may enter `Faulted`; cleanup still continues through `Stopping` to `Idle`. Start, stop, cancellation, and disposal are idempotent at their public boundaries. `StopAsync` signals a dedicated startup cancellation source before waiting for the lifecycle lock, so a user can interrupt keyframe, SOAP, or playback-confirmation waits; once `Playing` is reached, the caller's startup token no longer owns the media-pump lifetime. Renderer `Stop` is best effort, while local publisher shutdown and token invalidation are mandatory.

## Milestone 1 component flow

1. `SsdpDiscoveryService` asks the interface provider for eligible IPv4 LAN interfaces and performs three bounded searches per interface.
2. Responses are parsed, deduplicated by UDN, and matched against a securely downloaded Device Description.
3. `RendererControlContextResolver` selects the highest declared AVTransport and optional ConnectionManager service versions.
4. `StaticTestClipPublisher` selects a concrete route-derived IPv4, binds Kestrel to it, creates a 256-bit session token, and exposes the embedded clip.
5. `StaticMediaTestSession` generates DIDL-Lite, sends the exact declared SOAP service type, starts playback, and requires both an HTTP request and renderer `PLAYING` state.
6. UPnP error 714 during metadata submission permits exactly one controlled retry with empty metadata.

The embedded clip is immutable and memory-backed. The live token is never persisted and becomes invalid before the listener is disposed.

## Configuration and diagnostics

Non-secret defaults live in `appsettings.json` and bind to validated options. User choices are loaded before the initial device discovery and are saved atomically to `%LOCALAPPDATA%\\DLNAScreenCast\\user-settings.json` when the application closes. The persisted choices include the renderer UDN, preset or custom output resolution, aspect-ratio handling, cursor/audio switches, and a display topology signature; transient monitor handles and stream tokens are never persisted. A missing or malformed settings file falls back to safe defaults.

After the main window is activated, `MainViewModel.InitializeAsync` restores those choices and automatically runs the same bounded, cancellable discovery operation used by the manual refresh command. The refresh button remains available for devices that join the LAN later.

Every test/cast attempt has a correlation ID and a structured `CastFailure` containing stage, user message, original exception, protocol status, fallback advice, and safe diagnostic context.

The display picker refreshes one bounded, downscaled snapshot per active monitor when its
drop-down opens. Preview capture runs off the UI thread, is cancelled when a newer refresh
starts, and stops after the still images are produced; the live stream continues to use WGC.

SSDP datagrams, XML, SOAP, URIs, and renderer HTTP requests are untrusted. All network operations are cancellable, time-bounded, and size-bounded. XML DTDs and external entities are disabled. Full tokens, credentials, cookies, and media payloads must not enter diagnostics.

## Native boundary

The native boundary uses ABI v7 in `src/DesktopDlnaCast.Media.Native/include/ddc_media.h`. It defines explicit capture source/configuration fields, selectable aspect-ratio handling (stretch, center crop, or aspect-preserving black bars), optional audio, an explicit audio-cast profile, audio-only output and local-playback mute, bounded packet reads, random-access-point flags, video/audio/mute statistics, finite read timeouts, detailed UTF-8 errors, encoder diagnostics, and idempotent stop/destroy semantics. Diagnostics report the selected video and audio encoders, software/hardware flag, D3D11-versus-libswscale pixel backend, and accepted media parameters. The managed side uses `SafeHandle`; no native pointer or exception escapes into Core.

`LiveCastSession` owns the media session, media pump, continuous publisher, renderer control, and cleanup. It starts media before publishing, waits until a chunk marked as a complete PAT/PMT/codec-configuration/IDR start point is buffered, then sends the URI. A completed or failed media pump aborts playback startup.

While `Playing`, the session polls `GetTransportInfo` on the renderer at `TransportMonitorInterval`. After `TransportMonitorStoppedThreshold` consecutive `STOPPED`/`NO_MEDIA_PRESENT` observations, or `TransportMonitorFailureThreshold` consecutive probe failures (each bounded by `TransportMonitorCallTimeout`), the session stops itself and runs full cleanup. The result is exposed as `ICastSession.StopReason` (`UserRequested`, `RendererReportedStopped`, or `RendererUnreachable`); the GUI surfaces renderer-initiated stops in the status area. When the renderer is unreachable, cleanup skips the best-effort `AVTransport.Stop` call so local teardown is not delayed by the timeout.

The native CMake target contains exception-safe ABI exports, a bounded output queue, a free-threaded WGC capture source, hardware-first D3D11 BGRA-to-NV12 processing with letterbox/color-range control, a bounded `libswscale` CPU fallback, Media Foundation H.264 selection through FFmpeg `h264_mf`, and a custom-memory `libavformat` MPEG-TS muxer. WASAPI shared-mode loopback requests 48 kHz stereo PCM with the Windows channel matrix/resampler and emits timestamped silence when the endpoint has no data. Video sessions feed the Microsoft AAC-LC MFT through FFmpeg `aac_mf`. Audio-only sessions never create WGC, D3D11, or H.264 resources and can produce a native MP3 elementary stream through `mp3_mf`, AAC-LC with ADTS headers, RFC 2586 big-endian L16, or the legacy AAC-only MPEG-TS compatibility stream. The MP3 encoder uses a bounded staging buffer to reconcile 1024-sample capture blocks with 1152-sample MP3 frames. When requested, the same native owner mutes the captured render endpoint only after loopback has started, preserves its initial mute state, observes external mute changes, and restores an app-owned mute during every normal or exceptional cleanup path.

## Milestone status

- Milestone 0: complete—solution, project boundaries, DI, logging, configuration, localized WinUI shell, tests, docs, and Windows CI.
- Milestone 1: complete—active SSDP discovery, secure device/service resolution, capability probing, static MPEG-TS publisher, GUI test action, repository MockRenderer, failure injection, and authoritative end-to-end tests.
- Milestone 2: complete—the managed continuous publisher, bounded keyframe-aware buffer, interruptible live orchestration, WGC display capture, 720p30/1080p30 selection, native x64 build, two-minute MockRenderer pull, and 20-cycle lifecycle acceptance runs are implemented.
- Milestone 3: in progress—WASAPI loopback, AAC-LC, A/V MPEG-TS muxing, silence generation, default-device reopen, video-only initialization fallback, ABI v4 diagnostics, GUI audio/local-mute selection, strict H.264/AAC stream validation, and a 120-second silent-host MockRenderer A/V run are implemented. Audible perceptual synchronization, physical verification that pre-volume loopback remains audible while the endpoint is muted, and a live default-device-change acceptance run remain outstanding.
