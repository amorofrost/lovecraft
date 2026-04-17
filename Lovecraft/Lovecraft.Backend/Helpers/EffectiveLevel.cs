using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Helpers;

/// <summary>
/// Unified level map spanning user ranks (0–3) and staff roles (4–5).
/// A user's effective level = max(rank level, staff role level).
/// </summary>
public static class EffectiveLevel
{
    public const int Novice = 0;
    public const int ActiveMember = 1;
    public const int FriendOfAloe = 2;
    public const int AloeCrew = 3;
    public const int Moderator = 4;
    public const int Admin = 5;

    public static int Parse(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "novice" or "none" or "" or null => Novice,
            "activemember" => ActiveMember,
            "friendofaloe" => FriendOfAloe,
            "aloecrew" => AloeCrew,
            "moderator" => Moderator,
            "admin" => Admin,
            _ => Novice,
        };

    public static int For(UserEntity user, UserRank computedRank)
    {
        var rankLevel = (int)computedRank;
        var staffLevel = Parse(user.StaffRole);
        return Math.Max(rankLevel, staffLevel);
    }
}
