using DesktopDlnaCast.Core.Models;

namespace DesktopDlnaCast.Core.Casting;

public static class AudioCastProfileSelector
{
    public static IReadOnlyList<AudioCastProfile> SelectCandidates(
        IReadOnlyList<string> sinkProtocolInfo)
    {
        ArgumentNullException.ThrowIfNull(sinkProtocolInfo);
        List<AudioCastProfile> candidates = [];

        foreach (string entry in sinkProtocolInfo)
        {
            if (Matches(entry, AudioCastProfile.Mp3))
            {
                Add(candidates, AudioCastProfile.Mp3);
            }
            else if (Matches(entry, AudioCastProfile.AacAdts))
            {
                Add(candidates, AudioCastProfile.AacAdts);
            }
            else if (Matches(entry, AudioCastProfile.Lpcm))
            {
                Add(candidates, AudioCastProfile.Lpcm);
            }
        }

        // Renderer capability lists are frequently incomplete. Keep a finite,
        // deterministic fallback order after every explicitly advertised format.
        Add(candidates, AudioCastProfile.Mp3);
        Add(candidates, AudioCastProfile.AacAdts);
        Add(candidates, AudioCastProfile.Lpcm);
        Add(candidates, AudioCastProfile.AacMpegTsCompatibility);
        return candidates;
    }

    private static bool Matches(string entry, AudioCastProfile profile)
    {
        string value = entry.Trim();
        string[] fields = value.Split(':', 4);
        string contentFormat = fields.Length == 4 ? fields[2] : string.Empty;
        return profile switch
        {
            AudioCastProfile.Mp3 =>
                contentFormat.Equals("audio/mpeg", StringComparison.OrdinalIgnoreCase) ||
                contentFormat.Equals("audio/mp3", StringComparison.OrdinalIgnoreCase) ||
                contentFormat.Equals("audio/mpeg3", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("DLNA.ORG_PN=MP3", StringComparison.OrdinalIgnoreCase),
            AudioCastProfile.AacAdts =>
                contentFormat.Equals("audio/aac", StringComparison.OrdinalIgnoreCase) ||
                contentFormat.Equals("audio/vnd.dlna.adts", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("DLNA.ORG_PN=AAC_ADTS", StringComparison.OrdinalIgnoreCase),
            AudioCastProfile.Lpcm =>
                contentFormat.StartsWith("audio/L16", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("DLNA.ORG_PN=LPCM", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static void Add(List<AudioCastProfile> candidates, AudioCastProfile profile)
    {
        if (!candidates.Contains(profile))
        {
            candidates.Add(profile);
        }
    }
}
