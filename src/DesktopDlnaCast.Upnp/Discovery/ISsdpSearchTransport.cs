namespace DesktopDlnaCast.Upnp.Discovery;

public interface ISsdpSearchTransport
{
    Task<IReadOnlyList<SsdpDatagram>> SearchAsync(
        LanNetworkInterface networkInterface,
        string searchTarget,
        int maximumWaitSeconds,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

