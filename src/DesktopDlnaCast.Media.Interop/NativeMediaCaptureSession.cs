using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.Media.Interop.Native;

namespace DesktopDlnaCast.Media.Interop;

internal sealed class NativeMediaCaptureSession(NativeMediaSessionHandle handle) : IMediaCaptureSession
{
    private const int MaximumPacketBytes = 1024 * 1024;
    private const int RandomAccessPointFlag = 0x1;
    private int started;
    private int stopped;
    private int disposed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            return Task.CompletedTask;
        }

        NativeResult result = NativeMethods.ddc_session_start(handle);
        if (result != NativeResult.Success)
        {
            Volatile.Write(ref started, 0);
            return Task.FromException(CreateException(result));
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<MediaStreamChunk> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Volatile.Read(ref started) == 0)
        {
            throw new InvalidOperationException("The native media session has not started.");
        }

        byte[] buffer = new byte[MaximumPacketBytes];
        while (Volatile.Read(ref stopped) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeResult result = NativeMethods.ddc_session_read(
                handle,
                buffer,
                buffer.Length,
                out int bytesWritten,
                out long timestamp100Nanoseconds,
                out int packetFlags,
                timeoutMilliseconds: 250);
            switch (result)
            {
                case NativeResult.Success when bytesWritten is > 0 and <= MaximumPacketBytes:
                    yield return new(
                        buffer.AsMemory(0, bytesWritten).ToArray(),
                        TimeSpan.FromTicks(timestamp100Nanoseconds),
                        (packetFlags & RandomAccessPointFlag) != 0);
                    break;
                case NativeResult.Timeout:
                    await Task.Yield();
                    break;
                case NativeResult.EndOfStream or NativeResult.Canceled:
                    yield break;
                case NativeResult.Success:
                    throw new NativeMediaException(
                        (int)NativeResult.InternalFailure,
                        "The native media session returned an invalid packet length.");
                default:
                    throw CreateException(result);
            }
        }
    }

    public MediaSessionStatistics GetStatistics()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        NativeSessionStatistics statistics = new()
        {
            StructSize = Marshal.SizeOf<NativeSessionStatistics>(),
            AbiVersion = NativeAbi.Version,
        };
        NativeResult result = NativeMethods.ddc_session_get_statistics(handle, ref statistics);
        ThrowIfFailed(result);
        return new(
            statistics.CapturedVideoFrames,
            statistics.EncodedVideoFrames,
            statistics.DroppedVideoFrames,
            statistics.CapturedAudioPackets,
            statistics.EncodedAudioFrames,
            statistics.AudioDeviceChanges,
            statistics.QueueOverflows,
            statistics.TimestampCorrections,
            TimeSpan.FromTicks(statistics.EncoderWait100Nanoseconds),
            statistics.LocalPlaybackMuteChanges,
            statistics.LocalPlaybackMuteRestoreFailures);
    }

    public MediaEncoderDiagnostics GetEncoderDiagnostics()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        NativeEncoderDiagnostics diagnostics = new()
        {
            StructSize = Marshal.SizeOf<NativeEncoderDiagnostics>(),
            AbiVersion = NativeAbi.Version,
        };
        byte[] encoderName = new byte[512];
        NativeResult result = NativeMethods.ddc_session_get_encoder_diagnostics(
            handle,
            ref diagnostics,
            encoderName,
            encoderName.Length,
            out int encoderNameBytesWritten);
        ThrowIfFailed(result);
        if (encoderNameBytesWritten < 0 || encoderNameBytesWritten > encoderName.Length)
        {
            throw new NativeMediaException(
                (int)NativeResult.InternalFailure,
                "The native media session returned an invalid encoder name length.");
        }

        return new(
            Encoding.UTF8.GetString(encoderName, 0, encoderNameBytesWritten),
            diagnostics.IsHardware != 0,
            diagnostics.AcceptedWidth,
            diagnostics.AcceptedHeight,
            diagnostics.FrameRateNumerator,
            diagnostics.FrameRateDenominator,
            diagnostics.AcceptedVideoBitrate,
            diagnostics.H264Profile,
            diagnostics.AcceptedGopFrames,
            diagnostics.AcceptedBFrameCount,
            diagnostics.VideoProcessorBackend switch
            {
                1 => MediaVideoProcessorBackend.D3D11,
                2 => MediaVideoProcessorBackend.Libswscale,
                _ => MediaVideoProcessorBackend.Unknown,
            },
            diagnostics.AudioEnabled != 0,
            diagnostics.AcceptedAudioBitrate,
            diagnostics.AudioSampleRate,
            diagnostics.AudioChannels);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref stopped, 1) != 0 || handle.IsClosed || handle.IsInvalid)
        {
            return Task.CompletedTask;
        }

        NativeResult result = NativeMethods.ddc_session_stop(handle);
        return result is NativeResult.Success or NativeResult.Canceled
            ? Task.CompletedTask
            : Task.FromException(CreateException(result));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            handle.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void ThrowIfFailed(NativeResult result)
    {
        if (result != NativeResult.Success)
        {
            throw CreateException(result);
        }
    }

    private NativeMediaException CreateException(NativeResult result)
    {
        byte[] message = new byte[4096];
        string detail = result.ToString();
        NativeResult errorResult = NativeMethods.ddc_session_get_last_error(
            handle,
            message,
            message.Length,
            out int bytesWritten);
        if (errorResult == NativeResult.Success && bytesWritten is > 0 and < 4096)
        {
            detail = Encoding.UTF8.GetString(message, 0, bytesWritten);
        }

        return new((int)result, $"Native media operation failed ({(int)result}): {detail}");
    }
}
