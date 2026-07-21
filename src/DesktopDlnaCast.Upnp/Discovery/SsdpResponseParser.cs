using System.Text;

namespace DesktopDlnaCast.Upnp.Discovery;

public static class SsdpResponseParser
{
    public const int MaximumDatagramBytes = 16 * 1024;
    public const int MaximumHeaderLineLength = 4096;

    public static bool TryParse(
        ReadOnlySpan<byte> datagram,
        out SsdpResponse? response,
        out string? error)
    {
        response = null;
        error = null;
        if (datagram.IsEmpty || datagram.Length > MaximumDatagramBytes)
        {
            error = "The SSDP datagram is empty or exceeds the configured size limit.";
            return false;
        }

        if (!ContainsOnlyHeaderBytes(datagram))
        {
            error = "The SSDP datagram contains invalid control characters.";
            return false;
        }

        string text = Encoding.Latin1.GetString(datagram);
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || !lines[0].Trim().Equals("HTTP/1.1 200 OK", StringComparison.OrdinalIgnoreCase))
        {
            error = "The SSDP response status line is invalid.";
            return false;
        }

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 1; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd('\r');
            if (line.Length == 0)
            {
                break;
            }

            if (line.Length > MaximumHeaderLineLength)
            {
                error = "An SSDP header line exceeds the configured size limit.";
                return false;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                error = "An SSDP header is malformed.";
                return false;
            }

            string name = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (!IsValidHeaderName(name) || !headers.TryAdd(name, value))
            {
                error = "An SSDP header name is invalid or duplicated.";
                return false;
            }
        }

        if (!headers.TryGetValue("LOCATION", out string? locationValue) ||
            !Uri.TryCreate(locationValue, UriKind.Absolute, out Uri? location) ||
            (location.Scheme != Uri.UriSchemeHttp && location.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(location.UserInfo))
        {
            error = "The SSDP LOCATION header is missing or invalid.";
            return false;
        }

        if (!headers.TryGetValue("USN", out string? usn) || !SsdpUsn.TryGetUdn(usn, out string udn))
        {
            error = "The SSDP USN header is missing or invalid.";
            return false;
        }

        response = new(headers, location, usn, udn);
        return true;
    }

    private static bool ContainsOnlyHeaderBytes(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            if (value < 0x20 && value is not (byte)'\r' and not (byte)'\n' and not (byte)'\t')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidHeaderName(string name)
    {
        if (name.Length is 0 or > 128)
        {
            return false;
        }

        foreach (char character in name)
        {
            if (!char.IsAsciiLetterOrDigit(character) &&
                character is not '!' and not '#' and not '$' and not '%' and not '&' and not '\'' and
                    not '*' and not '+' and not '-' and not '.' and not '^' and not '_' and not '`' and
                    not '|' and not '~')
            {
                return false;
            }
        }

        return true;
    }
}
