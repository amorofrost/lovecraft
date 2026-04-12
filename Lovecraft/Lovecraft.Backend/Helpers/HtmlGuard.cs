using System.Text.RegularExpressions;

namespace Lovecraft.Backend.Helpers;

public static class HtmlGuard
{
    private static readonly Regex HtmlTagPattern = new(
        @"<[a-zA-Z!/?][^>]*>",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100)  // ReDoS guard
    );

    /// <summary>
    /// Returns true if <paramref name="value"/> contains any HTML tag.
    /// Returns true on regex timeout (treat pathological input as unsafe).
    /// </summary>
    public static bool ContainsHtml(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        try
        {
            return HtmlTagPattern.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }
}
