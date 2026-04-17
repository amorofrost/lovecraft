using Lovecraft.Backend.Helpers;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.Enums;
using Xunit;

namespace Lovecraft.UnitTests;

public class RankCalculatorTests
{
    private static readonly RankThresholds T = RankThresholds.Defaults;

    private static UserEntity U(int replies = 0, int likes = 0, int events = 0, int matches = 0,
                                string? rankOverride = null) =>
        new()
        {
            ReplyCount = replies,
            LikesReceived = likes,
            EventsAttended = events,
            MatchCount = matches,
            RankOverride = rankOverride,
        };

    [Fact]
    public void Fresh_User_IsNovice() =>
        Assert.Equal(UserRank.Novice, RankCalculator.Compute(U(), T));

    [Fact]
    public void ActiveReplies_Threshold_PromotesToActive() =>
        Assert.Equal(UserRank.ActiveMember, RankCalculator.Compute(U(replies: T.ActiveReplies), T));

    [Fact]
    public void ActiveLikes_Threshold_PromotesToActive() =>
        Assert.Equal(UserRank.ActiveMember, RankCalculator.Compute(U(likes: T.ActiveLikes), T));

    [Fact]
    public void ActiveEvents_Threshold_PromotesToActive() =>
        Assert.Equal(UserRank.ActiveMember, RankCalculator.Compute(U(events: T.ActiveEvents), T));

    [Fact]
    public void FriendReplies_Threshold_PromotesToFriend() =>
        Assert.Equal(UserRank.FriendOfAloe, RankCalculator.Compute(U(replies: T.FriendReplies), T));

    [Fact]
    public void FriendLikes_Threshold_PromotesToFriend() =>
        Assert.Equal(UserRank.FriendOfAloe, RankCalculator.Compute(U(likes: T.FriendLikes), T));

    [Fact]
    public void CrewReplies_Threshold_PromotesToCrew() =>
        Assert.Equal(UserRank.AloeCrew, RankCalculator.Compute(U(replies: T.CrewReplies), T));

    [Fact]
    public void CrewMatches_Only_PromotesToCrew()
    {
        Assert.Equal(UserRank.AloeCrew, RankCalculator.Compute(U(matches: T.CrewMatches), T));
    }

    [Fact]
    public void FriendTierCriteria_WithoutCrewCriteria_StaysAtFriend() =>
        Assert.Equal(UserRank.FriendOfAloe,
            RankCalculator.Compute(U(replies: T.FriendReplies, matches: T.CrewMatches - 1), T));

    [Fact]
    public void OR_Logic_AnySingleCriterionSuffices()
    {
        Assert.Equal(UserRank.FriendOfAloe, RankCalculator.Compute(U(events: T.FriendEvents), T));
    }

    [Fact]
    public void TopDown_CrewCheckedBeforeFriend()
    {
        Assert.Equal(UserRank.AloeCrew,
            RankCalculator.Compute(U(replies: T.CrewReplies, events: T.FriendEvents), T));
    }

    [Fact]
    public void RankOverride_TakesPrecedence()
    {
        Assert.Equal(UserRank.AloeCrew,
            RankCalculator.Compute(U(rankOverride: "aloeCrew"), T));
    }

    [Fact]
    public void NullOverride_FallsBackToComputed()
    {
        Assert.Equal(UserRank.Novice,
            RankCalculator.Compute(U(rankOverride: null), T));
    }

    [Fact]
    public void JustBelowActiveThreshold_StaysNovice() =>
        Assert.Equal(UserRank.Novice,
            RankCalculator.Compute(U(replies: T.ActiveReplies - 1, likes: T.ActiveLikes - 1), T));
}
