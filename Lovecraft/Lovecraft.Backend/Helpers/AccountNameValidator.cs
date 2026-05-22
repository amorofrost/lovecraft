using System.Text.RegularExpressions;

namespace Lovecraft.Backend.Helpers;

public enum AccountNameValidationResult
{
    Ok,
    InvalidFormat,
    Reserved,
}

/// <summary>
/// Validates and normalizes user-chosen account names (logins). Telegram-style format:
/// 5–32 chars, starts with a letter, [A-Za-z0-9_]. Rejects a fixed reserved-name list.
/// </summary>
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

    /// <summary>Returns Ok / InvalidFormat / Reserved for the given raw input (trimmed).</summary>
    public static AccountNameValidationResult Validate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return AccountNameValidationResult.InvalidFormat;
        var trimmed = raw.Trim();
        if (!Pattern.IsMatch(trimmed)) return AccountNameValidationResult.InvalidFormat;
        // Reserved HashSet uses OrdinalIgnoreCase, so casing is folded here.
        if (Reserved.Contains(trimmed)) return AccountNameValidationResult.Reserved;
        return AccountNameValidationResult.Ok;
    }

    /// <summary>Returns the canonical (trimmed + lowercased) form. Empty input → empty string.</summary>
    public static string Normalize(string? raw) =>
        string.IsNullOrEmpty(raw) ? string.Empty : raw.Trim().ToLowerInvariant();
}
