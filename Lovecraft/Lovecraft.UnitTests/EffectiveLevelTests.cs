using Lovecraft.Backend.Helpers;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.Enums;
using Xunit;

namespace Lovecraft.UnitTests;

public class EffectiveLevelTests
{
    [Theory]
    [InlineData("novice", 0)]
    [InlineData("activeMember", 1)]
    [InlineData("friendOfAloe", 2)]
    [InlineData("aloeCrew", 3)]
    [InlineData("moderator", 4)]
    [InlineData("admin", 5)]
    [InlineData("none", 0)]
    [InlineData("NOVICE", 0)]
    public void Parse_ReturnsExpectedLevel(string input, int expected)
    {
        Assert.Equal(expected, EffectiveLevel.Parse(input));
    }

    [Fact]
    public void Parse_UnknownValue_ReturnsZero()
    {
        Assert.Equal(0, EffectiveLevel.Parse("potato"));
    }

    [Fact]
    public void For_NoviceWithNoStaffRole_ReturnsZero()
    {
        var user = new UserEntity { StaffRole = "none" };
        Assert.Equal(0, EffectiveLevel.For(user, UserRank.Novice));
    }

    [Fact]
    public void For_NoviceModerator_ReturnsModeratorLevel()
    {
        var user = new UserEntity { StaffRole = "moderator" };
        Assert.Equal(4, EffectiveLevel.For(user, UserRank.Novice));
    }

    [Fact]
    public void For_AloeCrewNoStaff_ReturnsThree()
    {
        var user = new UserEntity { StaffRole = "none" };
        Assert.Equal(3, EffectiveLevel.For(user, UserRank.AloeCrew));
    }

    [Fact]
    public void For_AloeCrewAdmin_ReturnsAdminLevel()
    {
        var user = new UserEntity { StaffRole = "admin" };
        Assert.Equal(5, EffectiveLevel.For(user, UserRank.AloeCrew));
    }
}
