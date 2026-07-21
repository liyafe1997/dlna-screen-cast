# Static test media

`test-pattern.ts` is the deterministic Milestone 1 receiver-test asset served by the embedded HTTP publisher. It contains approximately two seconds of a 640x360, 30 fps H.264 Main test pattern and stereo 48 kHz AAC-LC audio in MPEG-TS.

Development regeneration command:

```powershell
ffmpeg -f lavfi -i "testsrc2=size=640x360:rate=30" -f lavfi -i "sine=frequency=440:sample_rate=48000" -t 2 -c:v libx264 -profile:v main -pix_fmt yuv420p -g 30 -keyint_min 30 -sc_threshold 0 -bf 0 -b:v 1000k -c:a aac -b:a 128k -ar 48000 -ac 2 -mpegts_flags +resend_headers -muxdelay 0 -muxpreload 0 -f mpegts test-pattern.ts
```

The generated asset is checked in, so application and CI tests do not require FFmpeg. The repository inspector verifies TS packet synchronization, PAT, PMT, H.264, and AAC. Optional `ffprobe` evidence is recorded in `docs/compatibility.md`.
