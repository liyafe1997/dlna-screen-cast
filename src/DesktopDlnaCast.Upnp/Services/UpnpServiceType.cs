using System.Globalization;

namespace DesktopDlnaCast.Upnp.Services;

public readonly record struct UpnpServiceType(string Domain, string Name, int Version)
{
    public const string AvTransportName = "AVTransport";
    public const string ConnectionManagerName = "ConnectionManager";
    public const string RenderingControlName = "RenderingControl";

    public bool IsAvTransport => Name.Equals(AvTransportName, StringComparison.Ordinal);

    public bool IsConnectionManager => Name.Equals(ConnectionManagerName, StringComparison.Ordinal);

    public static bool TryParse(string? value, out UpnpServiceType serviceType)
    {
        serviceType = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] parts = value.Split(':');
        if (parts.Length != 5 ||
            !parts[0].Equals("urn", StringComparison.OrdinalIgnoreCase) ||
            !parts[2].Equals("service", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(parts[1]) ||
            string.IsNullOrWhiteSpace(parts[3]) ||
            !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out int version) ||
            version <= 0)
        {
            return false;
        }

        serviceType = new(parts[1], parts[3], version);
        return true;
    }

    public override string ToString() => $"urn:{Domain}:service:{Name}:{Version}";
}

