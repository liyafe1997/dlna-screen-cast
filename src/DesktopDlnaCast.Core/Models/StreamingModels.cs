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

public enum AudioCastProfile
{
    None = 0,
    AacAdts = 1,
    Mp3 = 2,
    Lpcm = 3,
    AacMpegTsCompatibility = 4,
}

public sealed record StreamPublication(
    Uri PublicUri,
    string RedactedUri,
    StreamMode Mode,
    string ContentType = "video/mpeg",
    string ProtocolInfo = "http-get:*:video/mpeg:*");

public sealed record LiveStreamPublishOptions(
    bool StartAtLiveEdge = false,
    AudioCastProfile AudioProfile = AudioCastProfile.None);

public sealed record StreamClientRequest(
    string Method,
    IPAddress RemoteAddress,
    DateTimeOffset ReceivedAt);

public sealed record MediaStreamChunk(
    ReadOnlyMemory<byte> Data,
    TimeSpan Timestamp,
    bool StartsAtRandomAccessPoint);
