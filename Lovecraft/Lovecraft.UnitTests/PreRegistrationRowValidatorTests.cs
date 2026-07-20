using Lovecraft.Backend.Helpers;
using Lovecraft.Common.DTOs.Admin;
using Xunit;

namespace Lovecraft.UnitTests;

public class PreRegistrationRowValidatorTests
{
    private static PreRegisterAttendeeDto Row(string username, string name = "Test Person") =>
        new() { TelegramUsername = username, Name = name };

    [Theory]
    [InlineData("Anna_Petrova", "anna_petrova")]
    [InlineData("@Anna_Petrova", "anna_petrova")]
    [InlineData("  @Anna_Petrova  ", "anna_petrova")]
    public void NormalizeUsername_StripsAtAndWhitespace_AndLowercases(string raw, string expected)
    {
        Assert.Equal(expected, PreRegistrationRowValidator.NormalizeUsername(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeUsername_EmptyInput_ReturnsEmpty(string? raw)
    {
        Assert.Equal(string.Empty, PreRegistrationRowValidator.NormalizeUsername(raw));
    }

    [Fact]
    public void Validate_GoodRow_ReturnsNoStatusAndNormalizedUserId()
    {
        var v = PreRegistrationRowValidator.Validate(Row("@Anna_Petrova", "Anna Petrova"));

        Assert.Null(v.Status);
        Assert.Equal("Anna_Petrova", v.Username);
        Assert.Equal("anna_petrova", v.UserId);
        Assert.Equal("Anna Petrova", v.Name);
    }

    [Theory]
    [InlineData("abc")]        // too short (min 5)
    [InlineData("1nvalid")]    // must start with a letter
    [InlineData("bad-name")]   // hyphen not allowed
    public void Validate_MalformedUsername_ReturnsInvalidUsernameWithFormatMessage(string username)
    {
        var v = PreRegistrationRowValidator.Validate(Row(username));

        Assert.Equal(PreRegistrationRowValidator.StatusInvalidUsername, v.Status);
        Assert.Equal("invalidFormat", v.Message);
    }

    [Fact]
    public void Validate_ReservedUsername_ReturnsInvalidUsernameWithReservedMessage()
    {
        var v = PreRegistrationRowValidator.Validate(Row("official"));

        Assert.Equal(PreRegistrationRowValidator.StatusInvalidUsername, v.Status);
        Assert.Equal("reserved", v.Message);
    }

    [Theory]
    [InlineData("<b>bold</b>")]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingOrHtmlName_ReturnsInvalidName(string name)
    {
        var v = PreRegistrationRowValidator.Validate(Row("Good_User", name));

        Assert.Equal(PreRegistrationRowValidator.StatusInvalidName, v.Status);
    }

    [Fact]
    public void Validate_ChecksUsernameBeforeName()
    {
        // Both fields are bad — username must win so the caller reports the root cause.
        var v = PreRegistrationRowValidator.Validate(Row("bad", "<b>x</b>"));

        Assert.Equal(PreRegistrationRowValidator.StatusInvalidUsername, v.Status);
    }
}
