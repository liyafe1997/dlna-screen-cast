namespace DesktopDlnaCast.Upnp.Discovery;

public interface ILanNetworkInterfaceProvider
{
    IReadOnlyList<LanNetworkInterface> GetEligibleInterfaces();
}

