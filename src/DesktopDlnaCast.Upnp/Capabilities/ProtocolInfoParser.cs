namespace DesktopDlnaCast.Upnp.Capabilities;

public static class ProtocolInfoParser
{
    public const int MaximumInputLength = 64 * 1024;
    public const int MaximumEntries = 512;

    public static IReadOnlyList<ProtocolInfoEntry> ParseSink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        if (value.Length > MaximumInputLength)
        {
            throw new FormatException("The protocolInfo value exceeds the configured size limit.");
        }

        string[] rawEntries = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (rawEntries.Length > MaximumEntries)
        {
            throw new FormatException("The protocolInfo value contains too many entries.");
        }

        List<ProtocolInfoEntry> entries = new(rawEntries.Length);
        foreach (string rawEntry in rawEntries)
        {
            string[] fields = rawEntry.Split(':', 4, StringSplitOptions.TrimEntries);
            if (fields.Length != 4 || fields.Any(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            entries.Add(new(fields[0], fields[1], fields[2], fields[3]));
        }

        return entries.AsReadOnly();
    }
}

