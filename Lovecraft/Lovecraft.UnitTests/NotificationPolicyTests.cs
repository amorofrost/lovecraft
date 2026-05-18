using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Xunit;

namespace Lovecraft.UnitTests;

public class NotificationPolicyTests
{
    private static NotificationPreferencesDto Defaults()
    {
        var prefs = new NotificationPreferencesDto();
        foreach (var type in Enum.GetNames<NotificationType>())
        {
            var key = char.ToLowerInvariant(type[0]) + type[1..];
            prefs.Matrix[key] = new Dictionary<string, bool>
            {
                { "inApp", true }, { "telegram", false }, { "webPush", false }, { "email", false }
            };
        }
        prefs.Frequency["inApp"]    = NotificationFrequency.Immediate;
        prefs.Frequency["telegram"] = NotificationFrequency.Immediate;
        prefs.Frequency["webPush"]  = NotificationFrequency.Immediate;
        prefs.Frequency["email"]    = NotificationFrequency.Daily;
        return prefs;
    }

    private static ChannelAvailability AllAvailable() => new()
    {
        TelegramLinked = true, EmailVerified = true, WebPushSubscribed = true,
    };

    [Fact]
    public void Default_prefs_returns_only_in_app_for_any_type()
    {
        var prefs = Defaults();
        var avail = AllAvailable();

        var result = NotificationPolicy.ResolveChannels(prefs, NotificationType.LikeReceived, avail);

        Assert.Single(result);
        Assert.Contains(NotificationChannel.InApp, result);
    }

    [Fact]
    public void Enabled_telegram_for_type_is_returned_when_available()
    {
        var prefs = Defaults();
        prefs.Matrix["likeReceived"]["telegram"] = true;

        var result = NotificationPolicy.ResolveChannels(prefs, NotificationType.LikeReceived, AllAvailable());

        Assert.Equal(2, result.Count);
        Assert.Contains(NotificationChannel.InApp, result);
        Assert.Contains(NotificationChannel.Telegram, result);
    }

    [Fact]
    public void Enabled_telegram_but_not_linked_skips_telegram()
    {
        var prefs = Defaults();
        prefs.Matrix["likeReceived"]["telegram"] = true;
        var avail = AllAvailable();
        avail.TelegramLinked = false;

        var result = NotificationPolicy.ResolveChannels(prefs, NotificationType.LikeReceived, avail);

        Assert.Single(result);
        Assert.Contains(NotificationChannel.InApp, result);
    }

    [Fact]
    public void Master_mute_returns_empty()
    {
        var prefs = Defaults();
        prefs.Matrix["likeReceived"]["telegram"] = true;
        prefs.Mute = true;

        var result = NotificationPolicy.ResolveChannels(prefs, NotificationType.LikeReceived, AllAvailable());

        Assert.Empty(result);
    }

    [Fact]
    public void Snooze_in_future_returns_empty()
    {
        var prefs = Defaults();
        prefs.MutedUntilUtc = DateTime.UtcNow.AddHours(1);

        var result = NotificationPolicy.ResolveChannels(prefs, NotificationType.LikeReceived, AllAvailable());

        Assert.Empty(result);
    }

    [Fact]
    public void Snooze_in_past_is_ignored()
    {
        var prefs = Defaults();
        prefs.MutedUntilUtc = DateTime.UtcNow.AddHours(-1);

        var result = NotificationPolicy.ResolveChannels(prefs, NotificationType.LikeReceived, AllAvailable());

        Assert.Single(result);
        Assert.Contains(NotificationChannel.InApp, result);
    }

    [Fact]
    public void Web_push_returned_only_when_subscribed()
    {
        var prefs = Defaults();
        prefs.Matrix["messageReceived"]["webPush"] = true;
        var avail = AllAvailable();
        avail.WebPushSubscribed = false;

        var result = NotificationPolicy.ResolveChannels(prefs, NotificationType.MessageReceived, avail);

        Assert.Single(result);
        Assert.DoesNotContain(NotificationChannel.WebPush, result);
    }

    [Fact]
    public void Email_returned_only_when_verified()
    {
        var prefs = Defaults();
        prefs.Matrix["matchCreated"]["email"] = true;
        var avail = AllAvailable();
        avail.EmailVerified = false;

        var result = NotificationPolicy.ResolveChannels(prefs, NotificationType.MatchCreated, avail);

        Assert.Single(result);
        Assert.DoesNotContain(NotificationChannel.Email, result);
    }

    [Fact]
    public void Missing_type_key_falls_back_to_in_app_only()
    {
        var prefs = new NotificationPreferencesDto();   // empty matrix

        var result = NotificationPolicy.ResolveChannels(prefs, NotificationType.RankUp, AllAvailable());

        Assert.Single(result);
        Assert.Contains(NotificationChannel.InApp, result);
    }
}
