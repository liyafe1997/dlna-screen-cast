# StreamProbe

StreamProbe is a bounded, headless MPEG-TS validator for files and HTTP live endpoints. It verifies:

- 188-byte MPEG-TS packet synchronization;
- PAT and PMT;
- H.264 and optional AAC stream declarations;
- monotonic video/audio PTS observations;
- SPS, PPS, and IDR presence;
- maximum observed IDR interval.

It prints one machine-readable JSON object and returns exit code `0` for a valid sample, `1` for invalid media/network failure, or `2` for invalid arguments.

```powershell
dotnet run --project tools/StreamProbe/DesktopDlnaCast.StreamProbe.csproj -c Release -- `
  --input http://192.168.1.10:51783/stream/token/live.ts `
  --require-audio true `
  --max-bytes 67108864 `
  --maximum-gop-ms 1500 `
  --timeout-seconds 15
```

HTTP redirects and cookies are disabled, response headers and bytes are bounded, and the input URL is not echoed into output. Core validation does not depend on `ffprobe`; that external oracle remains optional.
