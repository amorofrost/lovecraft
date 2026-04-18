namespace Lovecraft.Backend.Services;

/// <summary>Normalizes invite codes for storage and lookup (case-insensitive, trimmed).</summary>
public static class EventInviteNormalizer
{
    public static string Normalize(string plainCode)
    {
        if (string.IsNullOrWhiteSpace(plainCode))
            return string.Empty;
        return plainCode.Trim().ToUpperInvariant();
    }
}
