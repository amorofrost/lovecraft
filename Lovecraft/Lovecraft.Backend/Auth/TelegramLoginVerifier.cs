using System.Security.Cryptography;
using System.Text;
using Lovecraft.Common.DTOs.Auth;

namespace Lovecraft.Backend.Auth;

/// <summary>
/// Verifies Telegram Login Widget payloads per https://core.telegram.org/widgets/login#checking-authorization
/// </summary>
public static class TelegramLoginVerifier
{
    /// <summary>
    /// Reject auth payloads older than this. The widget issues a signature immediately before the
    /// redirect/callback, so a legitimate client consumes it within seconds. A short window narrows
    /// the replay horizon for payloads leaked via phishing, browser history sync, or stolen logs.
    /// </summary>
    public static TimeSpan MaxAuthAge { get; } = TimeSpan.FromMinutes(5);

    public static bool Verify(string botToken, TelegramLoginRequestDto dto)
    {
        if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(dto.Hash))
            return false;

        var unixNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (dto.AuthDate <= 0 || unixNow - dto.AuthDate > MaxAuthAge.TotalSeconds)
            return false;

        var dataCheckString = BuildDataCheckString(dto);
        return VerifyDataCheckString(botToken, dataCheckString, dto.Hash);
    }

    /// <summary>Computes the expected login hash (for tests and debugging).</summary>
    public static string ComputeLoginHashHex(string botToken, TelegramLoginRequestDto dto)
    {
        var dataCheckString = BuildDataCheckString(dto);
        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));
        using var hmac = new HMACSHA256(secretKey);
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));
        return Convert.ToHexString(computed).ToLowerInvariant();
    }

    private static string BuildDataCheckString(TelegramLoginRequestDto dto)
    {
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("auth_date", dto.AuthDate.ToString()),
            new("first_name", dto.FirstName),
            new("id", dto.Id.ToString())
        };

        if (!string.IsNullOrEmpty(dto.LastName))
            pairs.Add(new KeyValuePair<string, string>("last_name", dto.LastName));
        if (!string.IsNullOrEmpty(dto.Username))
            pairs.Add(new KeyValuePair<string, string>("username", dto.Username));
        if (!string.IsNullOrEmpty(dto.PhotoUrl))
            pairs.Add(new KeyValuePair<string, string>("photo_url", dto.PhotoUrl));

        pairs.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return string.Join("\n", pairs.Select(p => $"{p.Key}={p.Value}"));
    }

    private static bool VerifyDataCheckString(string botToken, string dataCheckString, string receivedHashHex)
    {
        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));
        using var hmac = new HMACSHA256(secretKey);
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));

        // HMAC-SHA256 produces 32 bytes → 64 hex chars. Anything else is garbage input, so
        // skip the decode and return false — FixedTimeEquals itself requires equal lengths.
        var trimmed = receivedHashHex.Trim();
        if (trimmed.Length != computed.Length * 2)
            return false;

        byte[] received;
        try
        {
            received = Convert.FromHexString(trimmed);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(computed, received);
    }
}
