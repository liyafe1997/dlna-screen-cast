# Protocol notes

## Trust model

SSDP datagrams, Device Description XML, service URLs, SOAP responses, and renderer HTTP requests are untrusted LAN input. Implementations use bounded buffers, finite timeouts, cancellation, credential-free HTTP URLs, and XML readers with DTD/external entities disabled. HTTP redirects are disabled for UPnP control traffic.

## SSDP discovery

Discovery searches these targets on every eligible active IPv4 LAN interface:

```text
urn:schemas-upnp-org:device:MediaRenderer:1
urn:schemas-upnp-org:service:AVTransport:1
ssdp:all
```

Loopback, disconnected, Tunnel/PPP, named VPN/TUN/TAP adapters, wildcard, and IPv4 link-local addresses are excluded by default. Hyper-V, VMware, and VirtualBox guest adapters are not rejected by vendor name: when such an adapter is active and has a usable IPv4 address, it may be the guest's only real LAN route and must participate in discovery. Responses are capped at 16 KiB and must be valid `HTTP/1.1 200 OK` packets with a credential-free HTTP(S) `LOCATION` and UUID USN. Empty optional header values are accepted because compliant renderers such as Kodi return the standard `EXT:` header without a value; required headers remain strictly validated. Discovery deduplicates by UDN, not friendly name, and verifies that the Description UDN matches the SSDP identity.

## Description and service resolution

Device Description responses are capped at 1 MiB. Relative service URLs resolve against the Description URL. Control/event/SCPD URLs using another host, credentials, or unsupported schemes are rejected. AVTransport and ConnectionManager versions 1, 2, and 3 are recognized; the highest actually declared version is selected.

The renderer's exact service type is authoritative when creating `SOAPAction`. The implementation never rewrites all devices to AVTransport:1.

## SOAP and metadata

SOAP responses are capped at 256 KiB. AVTransport implements `SetAVTransportURI`, `Play` with speed `1`, `Stop`, and `GetTransportInfo`. ConnectionManager implements `GetProtocolInfo`; its sink list is advisory rather than an absolute compatibility gate.

SOAP envelopes and DIDL-Lite are written with `XmlWriter`. The DIDL resource protocol info matches the actual `video/mpeg` output. A renderer UPnP error 714 while setting non-empty metadata allows one retry with an empty `CurrentURIMetaData`; no other automatic loop is allowed.

## Static test playback sequence

1. Resolve services and optionally call `GetProtocolInfo`.
2. Start the embedded HTTP publisher on a concrete route-derived local IPv4.
3. Validate that the embedded MPEG-TS contains PAT, PMT, H.264, and AAC.
4. Generate a cryptographically random 256-bit URL token and DIDL-Lite.
5. Call `SetAVTransportURI`, then `Play`.
6. Wait for the renderer's HTTP GET or HEAD and poll `GetTransportInfo` until `PLAYING`.
7. On stop/failure, best-effort AVTransport `Stop`, invalidate the token, cancel HTTP work, and close the listener.

The HTTP endpoint is `GET|HEAD /stream/{token}/test.ts` with `Content-Type: video/mpeg`, explicit length for the static asset, and `Cache-Control: no-store, no-cache`. Access is restricted to the selected renderer IP by default.

## Continuous MPEG-TS endpoint

The Milestone 2 managed endpoint is `GET|HEAD /stream/{token}/live.ts`. GET has no fixed `Content-Length`; Kestrel streams and flushes bounded media chunks until cancellation. HEAD returns the same MIME/cache headers without subscribing. Unsupported Range input is treated as a fresh `200 OK` live request.

ABI v4 live MPEG-TS carries H.264 video and, when enabled, AAC-LC stereo at 48 kHz. The muxer writes both streams against one monotonic session clock, repeats PAT/PMT through the transport stream, and keeps keyframe-start chunks self-contained for newly connected renderer clients. If WASAPI or AAC initialization fails, the session remains available as video-only and reports that choice in encoder diagnostics. The optional local-playback mute controls the captured Windows render endpoint, not the DLNA payload: loopback remains the AAC source while the physical endpoint is muted. This option is invalid for video-only sessions and is restored during stop, cancellation, device changes, and error cleanup.

The in-memory buffer is bounded simultaneously by bytes, media duration, and per-client queued chunks. Each start-point chunk is contractually required to begin with everything a new decoder needs: PAT/PMT, codec configuration, and an IDR access unit. A new GET snapshots only from the newest retained start point. A subscriber that cannot keep up is disconnected when its bounded queue fills; it cannot force global memory growth.

Two user-facing latency options adjust this behavior per session and are persisted in user settings. The GOP interval option (0.5 s / 1 s / 2 s at 30 fps) controls the encoder keyframe cadence and therefore how old the newest retained start point can be. The live-edge option (`LiveStreamPublishOptions.StartAtLiveEdge`) makes a new GET skip the backlog snapshot entirely: the subscription is accepted immediately but delivers nothing until the next start-point chunk is appended, trading up to one GOP of startup speed for up to one GOP less steady-state latency. Renderer-side pre-buffering remains outside sender control.

## Stream validation

`tools/StreamProbe` samples a bounded file or HTTP response and emits one JSON result. Its internal parser verifies packet alignment, PAT/PMT, H.264/AAC stream types, monotonic observed PTS, SPS/PPS, IDR count, and maximum IDR interval. Redirects and cookies are disabled, response headers/bytes are bounded, and the complete input URL is never echoed. CI does not require `ffprobe`.

## URL and diagnostic hygiene

URLs sent to renderers contain a concrete IPv4 selected from the route to that renderer. Loopback is allowed only by an explicit test option. Wildcard, localhost, hostname, VPN/tunnel, and IPv6 link-local publication addresses are invalid.

Session tokens are URL-safe and never logged in full. Diagnostic event values are bounded; token-shaped path segments are redacted. Authorization, proxy authorization, cookies, and set-cookie values are redacted. Media payloads are never logged.
