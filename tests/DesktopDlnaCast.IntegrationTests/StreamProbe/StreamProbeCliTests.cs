using System.Diagnostics;
using System.Net;
using System.Text.Json;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Models;
using DesktopDlnaCast.Streaming.Configuration;
using DesktopDlnaCast.Streaming.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DesktopDlnaCast.IntegrationTests.StreamProbe;

public sealed class StreamProbeCliTests
{
    [Fact]
    public async Task CliValidatesBoundedHttpMpegTsAndDoesNotEchoSessionToken()
    {
        await using StaticTestClipPublisher publisher = new(
            new LoopbackNetworkSelector(),
            Options.Create(new StreamingOptions
            {
                Port = 0,
                AllowLoopbackForTests = true,
                RestrictToRendererAddress = true,
            }),
            NullLogger<StaticTestClipPublisher>.Instance);
        StreamPublication publication = await publisher.StartAsync(CreateRenderer(), CancellationToken.None);
        string probeAssembly = Path.Combine(AppContext.BaseDirectory, "DesktopDlnaCast.StreamProbe.dll");
        Assert.True(File.Exists(probeAssembly), $"StreamProbe was not copied to {probeAssembly}.");
        using Process process = new()
        {
            StartInfo = new()
            {
                FileName = "dotnet",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.StartInfo.ArgumentList.Add(probeAssembly);
        process.StartInfo.ArgumentList.Add("--input");
        process.StartInfo.ArgumentList.Add(publication.PublicUri.AbsoluteUri);
        process.StartInfo.ArgumentList.Add("--require-audio");
        process.StartInfo.ArgumentList.Add("true");
        process.StartInfo.ArgumentList.Add("--maximum-gop-ms");
        process.StartInfo.ArgumentList.Add("1500");
        process.StartInfo.ArgumentList.Add("--timeout-seconds");
        process.StartInfo.ArgumentList.Add("10");
        Assert.True(process.Start());
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        string output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        string error = await process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);

        Assert.Equal(0, process.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        using JsonDocument result = JsonDocument.Parse(output);
        Assert.Equal("valid", result.RootElement.GetProperty("status").GetString());
        Assert.Equal("http", result.RootElement.GetProperty("source").GetString());
        Assert.True(result.RootElement.GetProperty("PtsMonotonic").GetBoolean());
        Assert.Equal(2, result.RootElement.GetProperty("IdrCount").GetInt64());
        Assert.Equal(90_000, result.RootElement.GetProperty("MaximumIdrInterval90Khz").GetInt64());
        string token = publication.PublicUri.Segments[^2].TrimEnd('/');
        Assert.DoesNotContain(token, output, StringComparison.Ordinal);
        StreamClientRequest request = await publisher.WaitForClientRequestAsync(
            TimeSpan.FromSeconds(2),
            CancellationToken.None);
        Assert.Equal(HttpMethod.Get.Method, request.Method);
    }

    private static RendererDevice CreateRenderer() =>
        new(
            "uuid:stream-probe-renderer",
            "StreamProbe Renderer",
            "DesktopDlnaCast",
            "Probe",
            IPAddress.Loopback,
            new("http://127.0.0.1/device.xml"));

    private sealed class LoopbackNetworkSelector : INetworkInterfaceSelector
    {
        public Task<IPAddress> SelectLocalAddressAsync(
            IPAddress rendererAddress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(IPAddress.Loopback);
        }
    }
}
