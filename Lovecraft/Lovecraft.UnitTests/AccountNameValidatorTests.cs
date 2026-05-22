using Lovecraft.Backend.Helpers;
using Xunit;

namespace Lovecraft.UnitTests;

public class AccountNameValidatorTests
{
    [Theory]
    [InlineData("alice")]
    [InlineData("alice123")]
    [InlineData("Alice_Doe")]
    [InlineData("a1234")] // exactly 5 chars
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")] // exactly 32 chars
    public void Validate_AcceptsValidNames(string name)
    {
        Assert.Equal(AccountNameValidationResult.Ok, AccountNameValidator.Validate(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]               // too short
    [InlineData("abcd")]              // 4 chars, still too short
    [InlineData("a")]
    [InlineData("1alice")]            // starts with digit
    [InlineData("_alice")]            // starts with underscore
    [InlineData("alice-doe")]         // hyphen not allowed
    [InlineData("alice.doe")]         // dot not allowed
    [InlineData("alice doe")]         // space
    [InlineData("alice@doe")]         // @
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")] // 33 chars
    public void Validate_RejectsInvalidFormat(string name)
    {
        Assert.Equal(AccountNameValidationResult.InvalidFormat, AccountNameValidator.Validate(name));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Admin")]
    [InlineData("aloevera")]
    [InlineData("telegram")]
    [InlineData("system")]
    [InlineData("anonymous")]
    public void Validate_RejectsReservedNames(string name)
    {
        Assert.Equal(AccountNameValidationResult.Reserved, AccountNameValidator.Validate(name));
    }

    [Theory]
    [InlineData("Alice_Doe", "alice_doe")]
    [InlineData("  alice  ", "alice")]
    [InlineData("USER1", "user1")]
    public void Normalize_LowercasesAndTrims(string input, string expected)
    {
        Assert.Equal(expected, AccountNameValidator.Normalize(input));
    }
}
