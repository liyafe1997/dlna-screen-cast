using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;

namespace DesktopDlnaCast.Core.Casting;

internal sealed class UnavailableMediaCaptureSessionFactory : IMediaCaptureSessionFactory
{
    public Task<IMediaCaptureSession> CreateAsync(
        MediaCaptureConfiguration configuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<IMediaCaptureSession>(new PlatformNotSupportedException(
            "The DesktopDlnaCast native media component is not installed."));
    }
}
