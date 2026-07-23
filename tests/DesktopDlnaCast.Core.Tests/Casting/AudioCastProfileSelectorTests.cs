using DesktopDlnaCast.Core.Casting;
using DesktopDlnaCast.Core.Models;
using Xunit;

namespace DesktopDlnaCast.Core.Tests.Casting;

public sealed class AudioCastProfileSelectorTests
{
    [Fact]
    public void AdvertisedProfilesAreRankedBeforeGenericFallbacks()
    {
        IReadOnlyList<AudioCastProfile> candidates =
            AudioCastProfileSelector.SelectCandidates(
                ["http-get:*:audio/vnd.dlna.adts:DLNA.ORG_PN=AAC_ADTS"]);

        Assert.Equal(AudioCastProfile.AacAdts, candidates[0]);
        Assert.Contains(AudioCastProfile.Mp3, candidates);
        Assert.Equal(AudioCastProfile.AacMpegTsCompatibility, candidates[^1]);
    }

    [Fact]
    public void UnknownRendererGetsFiniteCompatibilityOrder()
    {
        IReadOnlyList<AudioCastProfile> candidates =
            AudioCastProfileSelector.SelectCandidates([]);

        Assert.Equal(
            [
                AudioCastProfile.Mp3,
                AudioCastProfile.AacAdts,
                AudioCastProfile.Lpcm,
                AudioCastProfile.AacMpegTsCompatibility,
            ],
            candidates);
    }

    [Fact]
    public void AdvertisedOrderIsPreservedAsRendererPreference()
    {
        IReadOnlyList<AudioCastProfile> candidates =
            AudioCastProfileSelector.SelectCandidates(
                [
                    "http-get:*:audio/L16:DLNA.ORG_PN=LPCM",
                    "http-get:*:audio/mpeg:DLNA.ORG_PN=MP3",
                ]);

        Assert.Equal(AudioCastProfile.Lpcm, candidates[0]);
        Assert.Equal(AudioCastProfile.Mp3, candidates[1]);
    }

    [Fact]
    public void MpegUrlIsNotMisclassifiedAsMp3()
    {
        IReadOnlyList<AudioCastProfile> candidates =
            AudioCastProfileSelector.SelectCandidates(
                [
                    "http-get:*:audio/mpegurl:*",
                    "http-get:*:audio/L16:*",
                    "http-get:*:audio/mpeg:*",
                ]);

        Assert.Equal(AudioCastProfile.Lpcm, candidates[0]);
        Assert.Equal(AudioCastProfile.Mp3, candidates[1]);
    }
}
