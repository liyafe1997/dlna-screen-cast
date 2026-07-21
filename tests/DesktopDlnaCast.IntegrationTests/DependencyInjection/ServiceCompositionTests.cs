using DesktopDlnaCast.Core.Casting;
using DesktopDlnaCast.Core.Configuration;
using DesktopDlnaCast.Core.DependencyInjection;
using DesktopDlnaCast.Streaming.DependencyInjection;
using DesktopDlnaCast.Upnp.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace DesktopDlnaCast.IntegrationTests.DependencyInjection;

public sealed class ServiceCompositionTests
{
    [Fact]
    public async Task HostStartsAndValidatesConfiguration()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{DesktopDlnaCastOptions.SectionName}:NetworkTimeout"] = "00:00:03",
        });
        builder.Services.AddDesktopDlnaCastCore(builder.Configuration);
        builder.Services.AddDesktopDlnaCastUpnp(builder.Configuration);
        builder.Services.AddDesktopDlnaCastStreaming(builder.Configuration);

        using IHost host = builder.Build();
        await host.StartAsync(CancellationToken.None);

        Assert.Same(
            host.Services.GetRequiredService<CastSessionStateMachine>(),
            host.Services.GetRequiredService<CastSessionStateMachine>());
        Assert.Equal(
            TimeSpan.FromSeconds(3),
            host.Services.GetRequiredService<IOptions<DesktopDlnaCastOptions>>().Value.NetworkTimeout);

        await host.StopAsync(CancellationToken.None);
    }
}
