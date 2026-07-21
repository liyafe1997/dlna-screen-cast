using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Casting;
using DesktopDlnaCast.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DesktopDlnaCast.Core.DependencyInjection;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopDlnaCastCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<DesktopDlnaCastOptions>()
            .Bind(configuration.GetSection(DesktopDlnaCastOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<DesktopDlnaCastOptions>, DesktopDlnaCastOptionsValidator>();
        services.AddSingleton<CastSessionStateMachine>();
        services
            .AddOptions<StaticMediaTestOptions>()
            .Bind(configuration.GetSection(StaticMediaTestOptions.SectionName));
        services.AddSingleton(provider =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<StaticMediaTestOptions>>().Value);
        services.AddSingleton<IStaticMediaTestSession, StaticMediaTestSession>();
        services
            .AddOptions<LiveCastOptions>()
            .Bind(configuration.GetSection(LiveCastOptions.SectionName));
        services.AddSingleton(provider =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LiveCastOptions>>().Value);
        services.TryAddSingleton<IMediaCaptureSessionFactory, UnavailableMediaCaptureSessionFactory>();
        services.AddSingleton<ICastSession, LiveCastSession>();
        return services;
    }
}
