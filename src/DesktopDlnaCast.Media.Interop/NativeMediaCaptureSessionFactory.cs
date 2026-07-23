using System.Runtime.InteropServices;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.Media.Interop.Native;

namespace DesktopDlnaCast.Media.Interop;

public sealed class NativeMediaCaptureSessionFactory : IMediaCaptureSessionFactory
{
    public Task<IMediaCaptureSession> CreateAsync(
        MediaCaptureConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        NativeStreamConfiguration native = new()
        {
            StructSize = Marshal.SizeOf<NativeStreamConfiguration>(),
            AbiVersion = NativeAbi.Version,
            SourceKind = (int)configuration.SourceKind,
            SourceHandle = checked((ulong)configuration.SourceHandle),
            IncludeCursor = configuration.IncludeCursor ? 1 : 0,
            Width = configuration.Width,
            Height = configuration.Height,
            FrameRate = configuration.FrameRate,
            VideoBitrate = configuration.VideoBitrate,
            GopFrames = configuration.GopFrames,
            AudioBitrate = configuration.AudioBitrate,
            IncludeAudio = configuration.IncludeAudio ? 1 : 0,
            StreamMode = (int)StreamMode.MpegTsContinuous,
            MuteLocalPlayback = configuration.MuteLocalPlayback ? 1 : 0,
            AudioOnly = configuration.AudioOnly ? 1 : 0,
            AudioProfile = (int)configuration.AudioProfile,
            AspectRatioMode = (int)configuration.AspectRatioMode,
        };
        NativeResult result = NativeMethods.ddc_session_create(in native, out NativeMediaSessionHandle handle);
        if (result != NativeResult.Success)
        {
            handle?.Dispose();
            throw new NativeMediaException((int)result, $"Native media session creation failed ({(int)result}).");
        }

        return Task.FromResult<IMediaCaptureSession>(new NativeMediaCaptureSession(handle));
    }
}
