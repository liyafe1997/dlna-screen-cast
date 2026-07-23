using DesktopDlnaCast.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DesktopDlnaCast.Media.Interop.DependencyInjection;

public static class MediaInteropServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopDlnaCastMediaInterop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton<IMediaCaptureSessionFactory, NativeMediaCaptureSessionFactory>());
        services.TryAddSingleton<IDisplayCaptureSourceProvider, WindowsDisplayCaptureSourceProvider>();
        services.TryAddSingleton<IDisplayPreviewProvider, WindowsDisplayPreviewProvider>();
        return services;
    }
}
