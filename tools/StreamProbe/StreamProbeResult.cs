namespace DesktopDlnaCast.StreamProbe;

public sealed record StreamProbeResult(
    long BytesRead,
    long PacketCount,
    bool PatSeen,
    bool PmtSeen,
    bool H264Seen,
    bool AacSeen,
    bool PtsMonotonic,
    long VideoPtsCount,
    long AudioPtsCount,
    long IdrCount,
    long MaximumIdrInterval90Khz,
    bool SpsSeen,
    bool PpsSeen);
