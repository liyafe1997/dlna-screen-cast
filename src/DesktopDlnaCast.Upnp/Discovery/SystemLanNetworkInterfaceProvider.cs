using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DesktopDlnaCast.Upnp.Discovery;

public sealed class SystemLanNetworkInterfaceProvider : ILanNetworkInterfaceProvider
{
    public IReadOnlyList<LanNetworkInterface> GetEligibleInterfaces()
    {
        List<LanNetworkInterface> result = [];
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!LanAddressPolicy.IsEligibleInterface(
                    networkInterface.NetworkInterfaceType,
                    networkInterface.OperationalStatus,
                    networkInterface.Name,
                    networkInterface.Description))
            {
                continue;
            }

            IPInterfaceProperties properties;
            IPv4InterfaceProperties ipv4Properties;
            try
            {
                properties = networkInterface.GetIPProperties();
                ipv4Properties = properties.GetIPv4Properties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                    LanAddressPolicy.IsUsableIpv4(unicast.Address))
                {
                    result.Add(new(
                        networkInterface.Id,
                        networkInterface.Name,
                        unicast.Address,
                        ipv4Properties.Index));
                }
            }
        }

        return result
            .DistinctBy(candidate => candidate.Address)
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Address.ToString(), StringComparer.Ordinal)
            .ToArray();
    }
}

