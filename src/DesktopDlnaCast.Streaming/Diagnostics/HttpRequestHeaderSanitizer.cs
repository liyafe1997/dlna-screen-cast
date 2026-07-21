using Microsoft.AspNetCore.Http;

namespace DesktopDlnaCast.Streaming.Diagnostics;

internal static class HttpRequestHeaderSanitizer
{
    private const int MaximumHeaders = 64;
    private const int MaximumValueLength = 512;

    public static IEnumerable<(string Name, string Value)> EnumerateSafe(IHeaderDictionary headers)
    {
        int count = 0;
        foreach ((string name, Microsoft.Extensions.Primitives.StringValues values) in headers)
        {
            if (count++ >= MaximumHeaders)
            {
                yield break;
            }

            string value = IsSensitive(name) ? "<redacted>" : values.ToString();
            yield return (name, value.Length > MaximumValueLength ? value[..MaximumValueLength] : value);
        }
    }

    private static bool IsSensitive(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase);
}
