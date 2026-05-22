using System.Text.RegularExpressions;

namespace Lovecraft.Backend.Helpers;

public enum AccountNameValidationResult
{
    Ok,
    InvalidFormat,
    Reserved,
}

public static class AccountNameValidator
{
    private static readonly Regex Pattern = new(
        @"^[A-Za-z][A-Za-z0-9_]{4,31}$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "root", "system", "support", "help", "api", "auth", "login", "logout",
        "register", "settings", "profile", "user", "users", "me", "you", "search", "feed",
        "friends", "talks", "aloevera", "aloeve", "aloeband", "telegram", "google",
        "official", "mod", "moderator", "staff", "undefined", "null", "anonymous", "bot",
    };

    public static AccountNameValidationResult Validate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return AccountNameValidationResult.InvalidFormat;
        var trimmed = raw.Trim();
        if (!Pattern.IsMatch(trimmed)) return AccountNameValidationResult.InvalidFormat;
        if (Reserved.Contains(trimmed)) return AccountNameValidationResult.Reserved;
        return AccountNameValidationResult.Ok;
    }

    public static string Normalize(string raw) => raw.Trim().ToLowerInvariant();
}
