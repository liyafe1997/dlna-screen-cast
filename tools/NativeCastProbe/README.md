# NativeCastProbe

`NativeCastProbe` is the headless end-to-end acceptance runner. It composes production WGC/WASAPI capture, H.264/AAC encoding, continuous Kestrel publishing, UPnP control, DIDL-Lite metadata, the session state machine, and the repository `MockRenderer`. Audio is enabled by default and can be disabled with `--include-audio false`; `--mute-local-playback true` enables the physical endpoint-mute acceptance path. Its redacted JSON includes selected backends and accepted encoder parameters. No media is written to disk.

After the native x64 preset and managed solution are built, run a ten-second smoke test:

```powershell
dotnet run --project tools/NativeCastProbe/DesktopDlnaCast.NativeCastProbe.csproj `
  -c Release --no-build -- `
  --duration-seconds 10
```

The Milestone 2 stability command is:

```powershell
dotnet run --project tools/NativeCastProbe/DesktopDlnaCast.NativeCastProbe.csproj `
  -c Release --no-build -- `
  --duration-seconds 120 `
  --width 1280 `
  --height 720 `
  --video-bitrate 3000000
```

Run the same production session through twenty successful lifecycle cycles in one process:

```powershell
dotnet run --project tools/NativeCastProbe/DesktopDlnaCast.NativeCastProbe.csproj `
  -c Release --no-build -- `
  --duration-seconds 2 `
  --iterations 20
```

The process succeeds only after `SetAVTransportURI → Play → HTTP GET → media validation → Stop`, with the session and renderer both returned to their stopped states. Pass `--reject-metadata` to exercise the single empty-metadata fallback. Output is one token-redacted JSON object suitable for an acceptance log.
