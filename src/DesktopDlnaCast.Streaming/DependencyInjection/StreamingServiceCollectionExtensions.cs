using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Streaming.Configuration;
using DesktopDlnaCast.Streaming.Network;
using DesktopDlnaCast.Streaming.Publishing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopDlnaCast.Streaming.DependencyInjection;

public static class StreamingServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopDlnaCastStreaming(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<StreamingOptions>()
            .Bind(configuration.GetSection(StreamingOptions.SectionName))
            .Validate(options => options.Port is >= 0 and <= 65535, "Streaming Port must be between 0 and 65535.")
            .Validate(
                options => options.LiveBufferBytes is >= 188 and <= 256 * 1024 * 1024,
                "Streaming LiveBufferBytes must be between 188 bytes and 256 MiB.")
            .Validate(
                options => options.LiveBufferDuration > TimeSpan.Zero &&
                    options.LiveBufferDuration <= TimeSpan.FromSeconds(30),
                "Streaming LiveBufferDuration must be between zero and 30 seconds.")
            .Validate(
                options => options.LiveSubscriberQueueChunks is >= 1 and <= 4096,
                "Streaming LiveSubscriberQueueChunks must be between 1 and 4096.")
            .ValidateOnStart();
        services.AddSingleton<INetworkInterfaceSelector, RouteNetworkInterfaceSelector>();
        services.AddSingleton<StaticTestClipPublisher>();
        services.AddSingleton<IStreamPublisher>(provider =>
            provider.GetRequiredService<StaticTestClipPublisher>());
        services.AddSingleton<ContinuousMpegTsPublisher>();
        services.AddSingleton<ILiveStreamPublisher>(provider =>
            provider.GetRequiredService<ContinuousMpegTsPublisher>());
        return services;
    }
}
