using System.Security.Cryptography;
using System.Text;

namespace Lovecraft.Backend.Services;

public static class EventInviteHasher
{
    public static string Hash(string plainCode, string pepper)
    {
        var key = Encoding.UTF8.GetBytes(pepper.Length >= 32 ? pepper[..32] : pepper.PadRight(32, 'x'));
        using var hmac = new HMACSHA256(key);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(plainCode.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
