namespace DesktopDlnaCast.Upnp.Description;

public interface IDeviceDescriptionClient
{
    Task<RendererDeviceDescription> GetAsync(Uri descriptionUri, CancellationToken cancellationToken);
}
