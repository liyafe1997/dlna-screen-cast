using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace DesktopDlnaCast.IntegrationTests.MockRenderer;

public sealed class MockRendererCliTests
{
    [Fact]
    public async Task CliPrintsMachineReadableReadinessAndStopsThroughTestApi()
    {
        string executable = Path.Combine(AppContext.BaseDirectory, "DesktopDlnaCast.MockRenderer.exe");
        Assert.True(File.Exists(executable), $"MockRenderer executable was not copied to {executable}.");
        using Process process = new()
        {
            StartInfo = new()
            {
                FileName = executable,
                Arguments = "--http-port 0 --ssdp-port 0 --method GET",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        Assert.True(process.Start());
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            string? readinessLine = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.False(string.IsNullOrWhiteSpace(readinessLine));
            using JsonDocument readiness = JsonDocument.Parse(readinessLine);
            Assert.Equal("ready", readiness.RootElement.GetProperty("eventType").GetString());
            Uri baseUri = new(readiness.RootElement.GetProperty("http").GetString()!);
            Assert.True(readiness.RootElement.GetProperty("ssdp").GetString()!.EndsWith(":0", StringComparison.Ordinal) is false);

            using HttpClient client = new();
            Assert.Equal("ready", (await client.GetFromJsonAsync<JsonElement>(new Uri(baseUri, "health"), timeout.Token))
                .GetProperty("status").GetString());
            using HttpResponseMessage shutdown = await client.PostAsync(
                new Uri(baseUri, "test/shutdown"),
                content: null,
                timeout.Token);
            Assert.Equal(System.Net.HttpStatusCode.Accepted, shutdown.StatusCode);
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}
