using System.Security.Cryptography;
using System.Text;

namespace Lovecraft.Common;

/// <summary>
/// Compact HMAC-SHA256 signed token used to authorize one-click email unsubscribe.
/// Format: {userIdBase64Url}.{expiresAtUnixSeconds}.{signatureBase64Url}
/// Signature = HMAC-SHA256(secret, "{userIdBase64Url}.{expiresAtUnixSeconds}")
/// </summary>
public static class UnsubscribeToken
{
    public static string Generate(string userId, string secret, DateTime expiresAtUtc)
    {
        var userIdEncoded = Base64UrlEncode(Encoding.UTF8.GetBytes(userId));
        var expiresUnix = new DateTimeOffset(expiresAtUtc, TimeSpan.Zero).ToUnixTimeSeconds().ToString();
        var payload = $"{userIdEncoded}.{expiresUnix}";
        var signature = ComputeHmac(payload, secret);
        return $"{payload}.{signature}";
    }

    public static bool TryVerify(string token, string secret, out string userId)
    {
        userId = string.Empty;
        if (string.IsNullOrEmpty(token)) return false;

        var parts = token.Split('.');
        if (parts.Length != 3) return false;

        var userIdEncoded = parts[0];
        var expiresUnix = parts[1];
        var providedSignature = parts[2];
        var payload = $"{userIdEncoded}.{expiresUnix}";
        var expectedSignature = ComputeHmac(payload, secret);

        // Constant-time compare
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature), Encoding.UTF8.GetBytes(providedSignature)))
            return false;

        if (!long.TryParse(expiresUnix, out var expires)) return false;
        if (DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime < DateTime.UtcNow) return false;

        try
        {
            userId = Encoding.UTF8.GetString(Base64UrlDecode(userIdEncoded));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ComputeHmac(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
