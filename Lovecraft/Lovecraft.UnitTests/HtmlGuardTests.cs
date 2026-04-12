using Lovecraft.Backend.Helpers;

namespace Lovecraft.UnitTests;

public class HtmlGuardTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Hello world", false)]
    [InlineData("5 < 10", false)]
    [InlineData("price < ", false)]
    [InlineData("<3", false)]
    [InlineData("<b>bold</b>", true)]
    [InlineData("<script>alert(1)</script>", true)]
    [InlineData("<SCRIPT>alert(1)</SCRIPT>", true)]
    [InlineData("<img src='x' onerror='alert(1)'>", true)]
    [InlineData("<br/>", true)]
    [InlineData("</div>", true)]
    [InlineData("<!DOCTYPE html>", true)]
    [InlineData("<!--comment-->", true)]
    public void ContainsHtml_ReturnsExpected(string? input, bool expected)
    {
        Assert.Equal(expected, HtmlGuard.ContainsHtml(input));
    }

    [Fact]
    public void ContainsHtml_MultilineInput_WithTag_ReturnsTrue()
    {
        var input = "hello\n<script>alert(1)</script>\nworld";
        Assert.True(HtmlGuard.ContainsHtml(input));
    }

    [Fact]
    public void ContainsHtml_MultilineInput_NoTag_ReturnsFalse()
    {
        var input = "hello\nworld\n5 < 10";
        Assert.False(HtmlGuard.ContainsHtml(input));
    }

    [Fact]
    public void ContainsHtml_HtmlEntityEncoded_ReturnsFalse()
    {
        // HTML entity encoding is not an HTML tag — should not be rejected
        Assert.False(HtmlGuard.ContainsHtml("&lt;script&gt;alert(1)&lt;/script&gt;"));
    }

    [Fact]
    public void ContainsHtml_DoesNotThrow_OnLongInput()
    {
        // Verify the method handles long strings without throwing (timeout is caught internally)
        var longInput = new string('<', 10000) + new string('>', 10000);
        var ex = Record.Exception(() => HtmlGuard.ContainsHtml(longInput));
        Assert.Null(ex); // Must not propagate RegexMatchTimeoutException
    }
}
