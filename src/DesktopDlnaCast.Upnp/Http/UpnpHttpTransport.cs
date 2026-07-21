using System.Net;
using DesktopDlnaCast.Core.Configuration;
using Microsoft.Extensions.Options;

namespace DesktopDlnaCast.Upnp.Http;

public sealed class UpnpHttpTransport : IDisposable
{
    private readonly SocketsHttpHandler handler;

    public UpnpHttpTransport(IOptions<DesktopDlnaCastOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        handler = new()
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = options.Value.NetworkTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            UseCookies = false,
        };
        Client = new(handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        handler.Dispose();
    }
}

