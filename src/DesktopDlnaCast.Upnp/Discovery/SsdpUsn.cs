namespace DesktopDlnaCast.Upnp.Discovery;

public static class SsdpUsn
{
    public const int MaximumLength = 512;

    public static bool TryGetUdn(string? usn, out string udn)
    {
        udn = string.Empty;
        if (string.IsNullOrWhiteSpace(usn) || usn.Length > MaximumLength)
        {
            return false;
        }

        int separator = usn.IndexOf("::", StringComparison.Ordinal);
        ReadOnlySpan<char> candidate = separator < 0 ? usn.AsSpan() : usn.AsSpan(0, separator);
        candidate = candidate.Trim();
        if (!candidate.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase) || candidate.Length <= 5)
        {
            return false;
        }

        foreach (char character in candidate)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        udn = candidate.ToString();
        return true;
    }
}

