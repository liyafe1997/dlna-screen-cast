using DesktopDlnaCast.Streaming.Validation;

namespace DesktopDlnaCast.StreamProbe;

public static class StreamProbeEngine
{
    public static async Task<StreamProbeResult> ProbeAsync(
        Stream source,
        bool requireAudio,
        long maximumBytes,
        TimeSpan maximumGop,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maximumBytes is < 188 or > 256L * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        if (maximumGop <= TimeSpan.Zero || maximumGop > TimeSpan.FromSeconds(10))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumGop));
        }

        MpegTsInspector inspector = new();
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        while (total < maximumBytes)
        {
            int maximumRead = (int)Math.Min(buffer.Length, maximumBytes - total);
            int read = await source.ReadAsync(buffer.AsMemory(0, maximumRead), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            inspector.Push(buffer.AsSpan(0, read));
            total += read;
            if (total % MpegTsInspector.PacketSize == 0 &&
                HasSufficientLiveEvidence(inspector, requireAudio))
            {
                break;
            }
        }

        inspector.Complete(requireAudio, requireTiming: true);
        long maximumAllowedInterval = checked((long)Math.Ceiling(maximumGop.TotalSeconds * 90_000));
        if (inspector.MaximumIdrInterval90Khz <= 0 ||
            inspector.MaximumIdrInterval90Khz > maximumAllowedInterval)
        {
            throw new InvalidDataException(
                $"The maximum observed IDR interval was {inspector.MaximumIdrInterval90Khz} ticks; " +
                $"the configured limit is {maximumAllowedInterval} ticks at 90 kHz.");
        }

        return new(
            total,
            inspector.PacketCount,
            inspector.PatSeen,
            inspector.PmtSeen,
            inspector.H264Seen,
            inspector.AacSeen,
            inspector.PtsMonotonic,
            inspector.VideoPtsCount,
            inspector.AudioPtsCount,
            inspector.IdrCount,
            inspector.MaximumIdrInterval90Khz,
            inspector.SpsSeen,
            inspector.PpsSeen);
    }

    private static bool HasSufficientLiveEvidence(MpegTsInspector inspector, bool requireAudio) =>
        inspector.PatSeen &&
        inspector.PmtSeen &&
        inspector.H264Seen &&
        (!requireAudio || inspector.AacSeen) &&
        inspector.PtsMonotonic &&
        inspector.VideoPtsCount > 0 &&
        (!requireAudio || inspector.AudioPtsCount > 0) &&
        inspector.IdrCount >= 2 &&
        inspector.SpsSeen &&
        inspector.PpsSeen;
}
