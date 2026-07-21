using DesktopDlnaCast.Streaming.Security;
using Xunit;

namespace DesktopDlnaCast.Streaming.Tests.Security;

public sealed class SessionTokenTests
{
    [Fact]
    public void CreateProducesUniqueUrlSafeTokens()
    {
        HashSet<string> values = [];

        for (int index = 0; index < 128; index++)
        {
            SessionToken token = SessionToken.Create();
            Assert.Equal(SessionToken.EncodedLength, token.Value.Length);
            Assert.True(SessionToken.TryParse(token.Value, out _));
            Assert.DoesNotContain('+', token.Value);
            Assert.DoesNotContain('/', token.Value);
            Assert.DoesNotContain('=', token.Value);
            Assert.True(values.Add(token.Value));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa+")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa=")]
    public void TryParseRejectsInvalidToken(string value)
    {
        Assert.False(SessionToken.TryParse(value, out _));
    }

    [Fact]
    public void ToStringDoesNotRevealFullToken()
    {
        SessionToken token = SessionToken.Create();

        string display = token.ToString();

        Assert.NotEqual(token.Value, display);
        Assert.DoesNotContain(token.Value, display, StringComparison.Ordinal);
    }

    [Fact]
    public void FixedTimeEqualsAcceptsOnlyExactWellFormedToken()
    {
        SessionToken token = SessionToken.Create();
        SessionToken different = SessionToken.Create();

        Assert.True(token.FixedTimeEquals(token.Value));
        Assert.False(token.FixedTimeEquals(different.Value));
        Assert.False(token.FixedTimeEquals("invalid"));
        Assert.False(token.FixedTimeEquals(null));
    }
}
