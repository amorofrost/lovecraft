using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Lovecraft.Common.DTOs.Auth;

namespace Lovecraft.Backend.Auth;

/// <summary>
/// Verifies Telegram Mini App <c>initData</c> query-string per
/// https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app
///
/// The scheme differs from the Login Widget: the secret key for the outer HMAC is
/// <c>HMAC-SHA256("WebAppData", botToken)</c> (not <c>SHA256(botToken)</c>).
/// </summary>
public static class TelegramInitDataValidator
{
    /// <summary>Maximum accepted age of the <c>auth_date</c> timestamp embedded in initData.</summary>
    public static TimeSpan MaxAuthAge { get; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Parse and verify an initData string. Returns the Telegram user when the hash is valid
    /// and the <c>auth_date</c> is within <see cref="MaxAuthAge"/>; null otherwise.
    /// </summary>
    public static TelegramUserInfoDto? Validate(string botToken, string initData)
    {
        if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(initData))
            return null;

        var parsed = HttpUtility.ParseQueryString(initData);
        var receivedHash = parsed["hash"];
        if (string.IsNullOrEmpty(receivedHash))
            return null;

        if (!long.TryParse(parsed["auth_date"], out var authDate) || authDate <= 0)
            return null;

        var unixNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (unixNow - authDate > MaxAuthAge.TotalSeconds)
            return null;

        var pairs = new List<KeyValuePair<string, string>>();
        foreach (string? key in parsed.Keys)
        {
            if (key == null || key == "hash")
                continue;
            pairs.Add(new KeyValuePair<string, string>(key, parsed[key] ?? string.Empty));
        }
        pairs.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        var dataCheckString = string.Join("\n", pairs.Select(p => $"{p.Key}={p.Value}"));

        var secretKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));
        var computed = HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString));

        var trimmed = receivedHash.Trim();
        if (trimmed.Length != computed.Length * 2)
            return null;

        byte[] received;
        try
        {
            received = Convert.FromHexString(trimmed);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(computed, received))
            return null;

        var userJson = parsed["user"];
        if (string.IsNullOrEmpty(userJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(userJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id))
                return null;

            var info = new TelegramUserInfoDto
            {
                Id = id,
                FirstName = root.TryGetProperty("first_name", out var fn) ? fn.GetString() ?? string.Empty : string.Empty,
                LastName = root.TryGetProperty("last_name", out var ln) ? ln.GetString() : null,
                Username = root.TryGetProperty("username", out var un) ? un.GetString() : null,
                PhotoUrl = root.TryGetProperty("photo_url", out var pu) ? pu.GetString() : null,
            };
            return info;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Build a signed initData string for tests. The <c>user</c> element is serialized as JSON.</summary>
    public static string BuildSigned(string botToken, TelegramUserInfoDto user, long authDate, string? queryId = null)
    {
        var userJson = JsonSerializer.Serialize(new
        {
            id = user.Id,
            first_name = user.FirstName,
            last_name = user.LastName,
            username = user.Username,
            photo_url = user.PhotoUrl,
        }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

        var pairs = new List<KeyValuePair<string, string>>
        {
            new("auth_date", authDate.ToString()),
            new("user", userJson),
        };
        if (!string.IsNullOrEmpty(queryId))
            pairs.Add(new KeyValuePair<string, string>("query_id", queryId));
        pairs.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

        var dataCheckString = string.Join("\n", pairs.Select(p => $"{p.Key}={p.Value}"));
        var secretKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));
        var hash = Convert.ToHexString(HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLowerInvariant();

        var sb = new StringBuilder();
        foreach (var p in pairs)
        {
            if (sb.Length > 0) sb.Append('&');
            sb.Append(HttpUtility.UrlEncode(p.Key));
            sb.Append('=');
            sb.Append(HttpUtility.UrlEncode(p.Value));
        }
        sb.Append("&hash=").Append(hash);
        return sb.ToString();
    }
}
