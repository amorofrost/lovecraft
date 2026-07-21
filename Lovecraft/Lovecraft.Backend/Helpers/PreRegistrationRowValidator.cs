using Lovecraft.Common.DTOs.Admin;

namespace Lovecraft.Backend.Helpers;

/// <summary>
/// Storage-agnostic validation for a single attendee pre-registration row. Shared by the Mock
/// and Azure <c>IAuthService</c> implementations so both apply identical rules and emit
/// identical status strings.
/// </summary>
public static class PreRegistrationRowValidator
{
    public const string StatusCreated = "created";
    public const string StatusSkippedExists = "skippedExists";
    public const string StatusInvalidUsername = "invalidUsername";
    public const string StatusInvalidName = "invalidName";
    public const string StatusError = "error";

    /// <summary>Validated row. A non-null <paramref name="Status"/> means the row is rejected,
    /// in which case <paramref name="UserId"/> and <paramref name="Name"/> are meaningless.</summary>
    public readonly record struct ValidatedRow(
        string Username, string UserId, string Name, string? Status, string? Message);

    /// <summary>Strips a leading '@' and surrounding whitespace, then lowercases.
    /// Returns an empty string when there is nothing usable.</summary>
    public static string NormalizeUsername(string? rawUsername)
    {
        if (string.IsNullOrWhiteSpace(rawUsername)) return string.Empty;
        return AccountNameValidator.Normalize(rawUsername.Trim().TrimStart('@'));
    }

    /// <summary>Applies username-format then name rules. Username is checked first so a row
    /// with two problems reports the root cause.</summary>
    public static ValidatedRow Validate(PreRegisterAttendeeDto row)
    {
        var username = (row.TelegramUsername ?? string.Empty).Trim().TrimStart('@');

        var validation = AccountNameValidator.Validate(username);
        if (validation != AccountNameValidationResult.Ok)
        {
            return new ValidatedRow(
                username, string.Empty, string.Empty, StatusInvalidUsername,
                validation == AccountNameValidationResult.Reserved ? "reserved" : "invalidFormat");
        }

        var name = (row.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) || HtmlGuard.ContainsHtml(name))
        {
            return new ValidatedRow(
                username, string.Empty, string.Empty, StatusInvalidName,
                "name is required and must not contain HTML");
        }

        return new ValidatedRow(username, AccountNameValidator.Normalize(username), name, null, null);
    }

    /// <summary>Returns the photo URL only when it is an absolute http(s) URL; otherwise null.
    /// Admin-supplied URLs reach a server-side image fetch, so non-http(s) schemes (file:, ftp:,
    /// gopher:, ...) and relative/garbage values must not be fetched (SSRF hardening; the design
    /// spec requires a well-formed http(s) URL).</summary>
    public static string? SanitizePhotoUrl(string? photoUrl)
    {
        if (string.IsNullOrWhiteSpace(photoUrl)) return null;
        var trimmed = photoUrl.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? trimmed
            : null;
    }
}
