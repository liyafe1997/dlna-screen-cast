using DesktopDlnaCast.Streaming.Publishing;
using DesktopDlnaCast.Streaming.Validation;
using Xunit;

namespace DesktopDlnaCast.Streaming.Tests.Validation;

public sealed class MpegTsInspectorTests
{
    [Fact]
    public void ControlledCancellationCanIgnoreOneTrailingPartialPacket()
    {
        byte[] clip = LoadEmbeddedTestClip();
        MpegTsInspector inspector = new();
        inspector.Push(clip);
        inspector.Push([0x47]);

        inspector.Complete(
            requireAudio: true,
            requireTiming: true,
            allowTrailingPartialPacket: true);
    }

    [Fact]
    public void CompletedResponseStillRejectsTrailingPartialPacket()
    {
        byte[] clip = LoadEmbeddedTestClip();
        MpegTsInspector inspector = new();
        inspector.Push(clip);
        inspector.Push([0x47]);

        Assert.Throws<InvalidDataException>(() => inspector.Complete(requireAudio: true));
    }

    private static byte[] LoadEmbeddedTestClip()
    {
        using Stream stream = typeof(StaticTestClipPublisher).Assembly.GetManifestResourceStream(
            "DesktopDlnaCast.Streaming.Assets.test-pattern.ts") ??
            throw new InvalidOperationException("The embedded test clip is missing.");
        using MemoryStream output = new();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
