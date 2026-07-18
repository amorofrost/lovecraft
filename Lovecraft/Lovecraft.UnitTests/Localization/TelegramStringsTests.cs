using System.Collections.Generic;
using Lovecraft.Common.Enums;
using Lovecraft.Common.Localization;
using Xunit;

namespace Lovecraft.UnitTests.Localization;

public class TelegramStringsTests
{
    public static IEnumerable<object[]> AllKeys => new[]
    {
        new object[] { TelegramStrings.LikeReceived },
        new object[] { TelegramStrings.MatchCreated },
        new object[] { TelegramStrings.MessageReceived },
        new object[] { TelegramStrings.ForumReply },
        new object[] { TelegramStrings.EventPublished },
        new object[] { TelegramStrings.EventReminder },
        new object[] { TelegramStrings.EventInvite },
        new object[] { TelegramStrings.RankUp },
        new object[] { TelegramStrings.DefaultNotification },
        new object[] { TelegramStrings.BtnOpenInApp },
        new object[] { TelegramStrings.BtnMute },
        new object[] { TelegramStrings.BotStart },
        new object[] { TelegramStrings.BotHelp },
        new object[] { TelegramStrings.BotMuteAck },
    };

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void Every_key_has_both_languages(string key)
    {
        Assert.False(string.IsNullOrWhiteSpace(TelegramStrings.Get(Language.Ru, key)));
        Assert.False(string.IsNullOrWhiteSpace(TelegramStrings.Get(Language.En, key)));
    }

    [Fact]
    public void Get_throws_for_unknown_key()
    {
        Assert.Throws<KeyNotFoundException>(() => TelegramStrings.Get(Language.Ru, "tg.does.not.exist"));
    }

    [Theory]
    [InlineData("novice", "Новичок", "Novice")]
    [InlineData("activeMember", "Активный участник", "Active Member")]
    [InlineData("friendOfAloe", "Друг AloeVera", "Friend of Aloe")]
    [InlineData("aloeCrew", "Команда AloeVera", "Aloe Crew")]
    public void GetRankName_localizes_known_ranks(string rank, string ru, string en)
    {
        Assert.Equal(ru, TelegramStrings.GetRankName(Language.Ru, rank));
        Assert.Equal(en, TelegramStrings.GetRankName(Language.En, rank));
    }

    [Fact]
    public void GetRankName_falls_back_to_raw_for_unknown_rank()
    {
        Assert.Equal("mysteryRank", TelegramStrings.GetRankName(Language.Ru, "mysteryRank"));
    }
}
