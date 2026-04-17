using Lovecraft.Backend.Services;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Helpers;

/// <summary>
/// Computes a user's current rank from activity counters and configurable thresholds.
/// Top-down OR logic — returns the highest tier for which any single criterion is met.
/// RankOverride (if set) takes precedence over the computed value.
/// </summary>
public static class RankCalculator
{
    public static UserRank Compute(UserEntity user, RankThresholds t)
    {
        if (!string.IsNullOrWhiteSpace(user.RankOverride) &&
            TryParseRank(user.RankOverride, out var overridden))
            return overridden;

        if (user.ReplyCount >= t.CrewReplies ||
            user.LikesReceived >= t.CrewLikes ||
            user.EventsAttended >= t.CrewEvents ||
            user.MatchCount >= t.CrewMatches)
            return UserRank.AloeCrew;

        if (user.ReplyCount >= t.FriendReplies ||
            user.LikesReceived >= t.FriendLikes ||
            user.EventsAttended >= t.FriendEvents)
            return UserRank.FriendOfAloe;

        if (user.ReplyCount >= t.ActiveReplies ||
            user.LikesReceived >= t.ActiveLikes ||
            user.EventsAttended >= t.ActiveEvents)
            return UserRank.ActiveMember;

        return UserRank.Novice;
    }

    private static bool TryParseRank(string value, out UserRank rank)
    {
        switch (value.ToLowerInvariant())
        {
            case "novice": rank = UserRank.Novice; return true;
            case "activemember": rank = UserRank.ActiveMember; return true;
            case "friendofaloe": rank = UserRank.FriendOfAloe; return true;
            case "aloecrew": rank = UserRank.AloeCrew; return true;
            default: rank = UserRank.Novice; return false;
        }
    }
}
