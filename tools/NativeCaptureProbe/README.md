# NativeCaptureProbe

`NativeCaptureProbe` is the bounded, headless native runtime acceptance entry point. It captures the primary display and optional WASAPI loopback audio through the production managed/native ABI, writes an explicitly requested MPEG-TS file, reports machine-readable video/audio/mute statistics plus the selected backends, and exercises idempotent Stop/Dispose cleanup. Audio is enabled by default; use `--include-audio false` for the Milestone 2 video-only case. Use `--mute-local-playback true` to verify pre-volume loopback capture and endpoint-mute restoration on a physical Windows audio device.

It never records unless `--output` is supplied, refuses to replace an existing file unless `--overwrite` is present, and limits a run to at most ten minutes. The output contains desktop pixels and must be treated as sensitive test data; keep it under an ignored build directory and delete it after validation.

After building the native preset and the managed solution, run a ten-second smoke capture and validate it without `ffprobe`:

```powershell
dotnet run --project tools/NativeCaptureProbe/DesktopDlnaCast.NativeCaptureProbe.csproj `
  -c Release --no-build -- `
  --output out/native-capture-smoke.ts `
  --duration-seconds 10

dotnet run --project tools/StreamProbe/DesktopDlnaCast.StreamProbe.csproj `
  -c Release --no-build -- `
  --input out/native-capture-smoke.ts `
  --require-audio true `
  --max-bytes 67108864 `
  --maximum-gop-ms 1500 `
  --timeout-seconds 15
```

The stability run uses `--duration-seconds 120`. This tool validates production capture/encode/mux behavior but does not replace the MockRenderer pull test or a perceptual A/V sync check.
