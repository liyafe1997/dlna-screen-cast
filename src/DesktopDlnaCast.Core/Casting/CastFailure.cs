namespace DesktopDlnaCast.Core.Casting;

public enum CastFailureStage
{
    Discovery,
    RendererProbe,
    MediaPipeline,
    StreamPublishing,
    TransportControl,
    PlaybackConfirmation,
    Cleanup,
}

public sealed record CastFailure(
    CastFailureStage Stage,
    string UserMessage,
    string? TechnicalDetail,
    int? ProtocolStatus,
    string? SuggestedFallback,
    Guid CorrelationId,
    Exception? Exception,
    IReadOnlyDictionary<string, string> DiagnosticContext);
