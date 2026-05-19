using Lovecraft.Common;
using Xunit;

namespace Lovecraft.UnitTests;

public class UnsubscribeTokenTests
{
    private const string TestSecret = "test-secret-32-chars-or-more-aaaa";

    [Fact]
    public void Generate_then_TryVerify_returns_original_userId()
    {
        var token = UnsubscribeToken.Generate("user-abc", TestSecret, DateTime.UtcNow.AddHours(1));

        Assert.True(UnsubscribeToken.TryVerify(token, TestSecret, out var userId));
        Assert.Equal("user-abc", userId);
    }

    [Fact]
    public void Tampered_token_fails_verification()
    {
        var token = UnsubscribeToken.Generate("user-abc", TestSecret, DateTime.UtcNow.AddHours(1));
        // Flip a char in the middle (in the signature portion)
        var tampered = token.Substring(0, token.Length - 5) + "xxxxx";

        Assert.False(UnsubscribeToken.TryVerify(tampered, TestSecret, out _));
    }

    [Fact]
    public void Expired_token_fails_verification()
    {
        var token = UnsubscribeToken.Generate("user-abc", TestSecret, DateTime.UtcNow.AddHours(-1));

        Assert.False(UnsubscribeToken.TryVerify(token, TestSecret, out _));
    }

    [Fact]
    public void Wrong_secret_fails_verification()
    {
        var token = UnsubscribeToken.Generate("user-abc", TestSecret, DateTime.UtcNow.AddHours(1));

        Assert.False(UnsubscribeToken.TryVerify(token, "different-secret-32-chars-or-more!", out _));
    }

    [Fact]
    public void Malformed_token_fails_verification()
    {
        Assert.False(UnsubscribeToken.TryVerify("not-a-token", TestSecret, out _));
        Assert.False(UnsubscribeToken.TryVerify("only.two", TestSecret, out _));
        Assert.False(UnsubscribeToken.TryVerify("", TestSecret, out _));
    }
}
