using DesktopDlnaCast.Upnp.Http;

namespace DesktopDlnaCast.Upnp.Description;

public sealed class DeviceDescriptionClient(HttpClient httpClient) : IDeviceDescriptionClient
{
    public const int MaximumResponseBytes = 1024 * 1024;

    public async Task<RendererDeviceDescription> GetAsync(
        Uri descriptionUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptionUri);
        using HttpRequestMessage request = new(HttpMethod.Get, descriptionUri);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        Uri finalUri = response.RequestMessage?.RequestUri ?? descriptionUri;
        if (!finalUri.Host.Equals(descriptionUri.Host, StringComparison.OrdinalIgnoreCase) ||
            finalUri.Scheme != descriptionUri.Scheme)
        {
            throw new HttpRequestException("The device description request was redirected to another origin.");
        }

        await using MemoryStream body = await BoundedHttpContentReader.ReadAsync(
            response.Content,
            MaximumResponseBytes,
            cancellationToken).ConfigureAwait(false);
        return DeviceDescriptionParser.Parse(body, finalUri);
    }
}
