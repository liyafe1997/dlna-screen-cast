using DesktopDlnaCast.App.ViewModels;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Configuration;
using DesktopDlnaCast.Core.DependencyInjection;
using DesktopDlnaCast.Media.Interop.DependencyInjection;
using DesktopDlnaCast.Streaming.DependencyInjection;
using DesktopDlnaCast.Upnp.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace DesktopDlnaCast.App;

public partial class App : Application
{
    private static readonly Action<ILogger, Exception?> LogHostShutdownTimeout =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1000, nameof(LogHostShutdownTimeout)),
            "Application host shutdown exceeded its timeout");

    private static readonly Action<ILogger, Exception?> LogSettingsSaveFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1001, nameof(LogSettingsSaveFailed)),
            "User settings could not be saved during application shutdown");

    private readonly IHost host;
    private Window? window;
    private int shutdownStarted;

    public App()
    {
        InitializeComponent();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Logging.AddDebug();
        builder.Services.AddDesktopDlnaCastCore(builder.Configuration);
        builder.Services.AddDesktopDlnaCastMediaInterop();
        builder.Services.AddDesktopDlnaCastUpnp(builder.Configuration);
        builder.Services.AddDesktopDlnaCastStreaming(builder.Configuration);
        builder.Services.AddSingleton<IUserSettingsStore>(provider => new JsonUserSettingsStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DLNAScreenCast",
                "user-settings.json"),
            provider.GetRequiredService<ILogger<JsonUserSettingsStore>>()));
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        host = builder.Build();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await host.StartAsync().ConfigureAwait(true);
        window = host.Services.GetRequiredService<MainWindow>();
        window.Closed += OnWindowClosed;
        window.Activate();
        await host.Services.GetRequiredService<MainViewModel>().InitializeAsync().ConfigureAwait(true);
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (Interlocked.Exchange(ref shutdownStarted, 1) != 0)
        {
            return;
        }

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        MainViewModel viewModel = host.Services.GetRequiredService<MainViewModel>();
        viewModel.CancelCurrentOperation();
        try
        {
            try
            {
                await viewModel.SaveSettingsAsync(timeout.Token).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogSettingsSaveFailed(host.Services.GetRequiredService<ILogger<App>>(), exception);
            }

            await host.StopAsync(timeout.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            LogHostShutdownTimeout(host.Services.GetRequiredService<ILogger<App>>(), null);
        }
        finally
        {
            if (host is IAsyncDisposable asyncHost)
            {
                await asyncHost.DisposeAsync().ConfigureAwait(true);
            }
            else
            {
                host.Dispose();
            }
        }
    }
}
