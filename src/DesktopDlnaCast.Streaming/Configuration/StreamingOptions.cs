namespace DesktopDlnaCast.Streaming.Configuration;

public sealed class StreamingOptions
{
    public const string SectionName = "Streaming";

    public int Port { get; set; }

    public bool AllowAllPrivateInterfaces { get; set; }

    public bool AllowLoopbackForTests { get; set; }

    public bool RestrictToRendererAddress { get; set; } = true;

    public int LiveBufferBytes { get; set; } = 12 * 1024 * 1024;

    public TimeSpan LiveBufferDuration { get; set; } = TimeSpan.FromSeconds(5);

    public int LiveSubscriberQueueChunks { get; set; } = 64;
}
