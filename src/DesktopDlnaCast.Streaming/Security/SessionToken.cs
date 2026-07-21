namespace DesktopDlnaCast.Streaming.Security;

public readonly record struct SessionToken
{
    public const int ByteLength = 32;
    public const int EncodedLength = 43;

    private SessionToken(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public string Redacted => Value.Length >= 8 ? $"{Value[..4]}…{Value[^4..]}" : "[redacted]";

    public static SessionToken Create()
    {
        Span<byte> bytes = stackalloc byte[ByteLength];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        string value = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return new(value);
    }

    public static bool TryParse(string? value, out SessionToken token)
    {
        token = default;
        if (value is null || value.Length != EncodedLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            bool isValid = character is >= 'A' and <= 'Z' or
                >= 'a' and <= 'z' or
                >= '0' and <= '9' or
                '-' or '_';
            if (!isValid)
            {
                return false;
            }
        }

        token = new(value);
        return true;
    }

    public bool FixedTimeEquals(string? candidate)
    {
        if (!TryParse(candidate, out SessionToken parsed))
        {
            return false;
        }

        ReadOnlySpan<byte> expectedBytes = System.Text.Encoding.ASCII.GetBytes(Value);
        ReadOnlySpan<byte> candidateBytes = System.Text.Encoding.ASCII.GetBytes(parsed.Value);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            expectedBytes,
            candidateBytes);
    }

    public override string ToString() => Redacted;
}
