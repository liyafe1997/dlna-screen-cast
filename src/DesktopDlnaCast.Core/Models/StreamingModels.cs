using System.Net;

namespace DesktopDlnaCast.Core.Models;

public enum StreamMode
{
    Automatic,
    MpegTsContinuous,
    Hls,
}

public enum QualityProfile
{
    Automatic,
    Compatible,
    Standard,
}

public sealed record StreamPublication(Uri PublicUri, string RedactedUri, StreamMode Mode);

public sealed record LiveStreamPublishOptions(bool StartAtLiveEdge = false);

public sealed record StreamClientRequest(
    string Method,
    IPAddress RemoteAddress,
    DateTimeOffset ReceivedAt);

public sealed record MediaStreamChunk(
    ReadOnlyMemory<byte> Data,
    TimeSpan Timestamp,
    bool StartsAtRandomAccessPoint);
