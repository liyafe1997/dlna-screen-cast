using System.Collections.ObjectModel;

namespace DesktopDlnaCast.Upnp.Discovery;

public sealed class SsdpResponse
{
    internal SsdpResponse(Dictionary<string, string> headers, Uri location, string usn, string udn)
    {
        Headers = new ReadOnlyDictionary<string, string>(headers);
        Location = location;
        Usn = usn;
        Udn = udn;
    }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public Uri Location { get; }

    public string Usn { get; }

    public string Udn { get; }

    public string? SearchTarget => Headers.GetValueOrDefault("ST");

    public string? Server => Headers.GetValueOrDefault("SERVER");
}

