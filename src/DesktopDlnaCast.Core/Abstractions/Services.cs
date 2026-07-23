using DesktopDlnaCast.Core.Casting;
using DesktopDlnaCast.Core.Models;

namespace DesktopDlnaCast.Core.Abstractions;

public interface IDlnaDiscoveryService
{
    IAsyncEnumerable<RendererDevice> DiscoverAsync(CancellationToken cancellationToken);
}

public interface IDlnaRendererClient
{
    Task SetTransportUriAsync(
        RendererDevice renderer,
        Uri streamUri,
        string metadata,
        CancellationToken cancellationToken);

    Task PlayAsync(RendererDevice renderer, CancellationToken cancellationToken);

    Task StopAsync(RendererDevice renderer, CancellationToken cancellationToken);

    Task<string> GetTransportStateAsync(RendererDevice renderer, CancellationToken cancellationToken);

    Task<int?> GetVolumeAsync(RendererDevice renderer, CancellationToken cancellationToken);

    Task SetVolumeAsync(RendererDevice renderer, int volume, CancellationToken cancellationToken);
}

public interface IRendererCapabilityProbe
{
    Task<IReadOnlyList<string>> GetSinkProtocolInfoAsync(
        RendererDevice renderer,
        CancellationToken cancellationToken);
}

public interface IStreamPublisher : IAsyncDisposable
{
    Task<StreamPublication> StartAsync(RendererDevice renderer, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task<StreamClientRequest> WaitForClientRequestAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public interface ILiveStreamPublisher : IAsyncDisposable
{
    Task<StreamPublication> StartAsync(
        RendererDevice renderer,
        LiveStreamPublishOptions publishOptions,
        CancellationToken cancellationToken);

    ValueTask PublishAsync(MediaStreamChunk chunk, CancellationToken cancellationToken);

    Task WaitForStartPointAsync(TimeSpan timeout, CancellationToken cancellationToken);

    void Complete(Exception? completionException = null);

    Task StopAsync(CancellationToken cancellationToken);

    Task<StreamClientRequest> WaitForClientRequestAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public interface IMediaCaptureSession : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<MediaStreamChunk> ReadAllAsync(CancellationToken cancellationToken);

    MediaSessionStatistics GetStatistics();

    MediaEncoderDiagnostics GetEncoderDiagnostics();

    Task StopAsync(CancellationToken cancellationToken);
}

public interface IMediaCaptureSessionFactory
{
    Task<IMediaCaptureSession> CreateAsync(
        MediaCaptureConfiguration configuration,
        CancellationToken cancellationToken);
}

public interface IDisplayCaptureSourceProvider
{
    IReadOnlyList<DisplayCaptureSource> GetDisplays();
}

public interface IUserSettingsStore
{
    Task<UserSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken);
}

public interface INetworkInterfaceSelector
{
    Task<System.Net.IPAddress> SelectLocalAddressAsync(
        System.Net.IPAddress rendererAddress,
        CancellationToken cancellationToken);
}

public interface IStreamMetadataFactory
{
    string CreateVideoItem(string title, StreamPublication publication);

    string CreateAudioItem(string title, StreamPublication publication);
}

public interface ICompatibilityProfileStore
{
    Task<string?> GetRendererOverrideAsync(string rendererKey, CancellationToken cancellationToken);
}

public interface IStaticMediaTestSession : IAsyncDisposable
{
    CastSessionState State { get; }

    CastFailure? Failure { get; }

    Task StartAsync(RendererDevice renderer, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public interface ICastSession : IAsyncDisposable
{
    CastSessionState State { get; }

    CastFailure? Failure { get; }

    CastStopReason StopReason { get; }

    MediaEncoderDiagnostics? EncoderDiagnostics { get; }

    Task StartAsync(
        RendererDevice renderer,
        MediaCaptureConfiguration configuration,
        CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
