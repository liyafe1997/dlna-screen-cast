namespace DesktopDlnaCast.MockRenderer.Diagnostics;

internal static class DiagnosticRedactor
{
    private const int TokenLength = 43;

    public static string RedactTokens(string value)
    {
        if (value.Length < TokenLength)
        {
            return value;
        }

        char[] characters = value.ToCharArray();
        int runStart = -1;
        for (int index = 0; index <= characters.Length; index++)
        {
            bool tokenCharacter = index < characters.Length && IsTokenCharacter(characters[index]);
            if (tokenCharacter && runStart < 0)
            {
                runStart = index;
            }

            if (tokenCharacter || runStart < 0)
            {
                continue;
            }

            int runLength = index - runStart;
            if (runLength == TokenLength && IsPathSegment(value, runStart, index))
            {
                for (int redactIndex = runStart + 4; redactIndex < index - 4; redactIndex++)
                {
                    characters[redactIndex] = '*';
                }
            }

            runStart = -1;
        }

        return new(characters);
    }

    private static bool IsTokenCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '-' or '_';

    private static bool IsPathSegment(string value, int start, int end) =>
        (start == 0 || value[start - 1] == '/') &&
        (end == value.Length || value[end] is '/' or '?' or '#');
}

