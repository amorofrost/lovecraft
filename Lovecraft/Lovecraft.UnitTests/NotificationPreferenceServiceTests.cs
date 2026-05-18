using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Xunit;

namespace Lovecraft.UnitTests;

public class NotificationPreferenceServiceTests
{
    [Fact]
    public async Task Get_returns_defaults_when_no_row_exists()
    {
        MockDataStore.NotificationPreferences.Clear();
        var svc = new MockNotificationPreferenceService();

        var prefs = await svc.GetPreferencesAsync("user-new");

        // 9 types in matrix, each with 4 channels
        Assert.Equal(9, prefs.Matrix.Count);
        foreach (var kvp in prefs.Matrix)
        {
            Assert.True(kvp.Value["inApp"], $"inApp should default true for {kvp.Key}");
            Assert.False(kvp.Value["telegram"]);
            Assert.False(kvp.Value["webPush"]);
            Assert.False(kvp.Value["email"]);
        }
        Assert.Equal(NotificationFrequency.Immediate, prefs.Frequency["inApp"]);
        Assert.Equal(NotificationFrequency.Immediate, prefs.Frequency["telegram"]);
        Assert.Equal(NotificationFrequency.Immediate, prefs.Frequency["webPush"]);
        Assert.Equal(NotificationFrequency.Daily, prefs.Frequency["email"]);
        Assert.Equal(9, prefs.DailyDigestHourUtc);
        Assert.False(prefs.Mute);
        Assert.Null(prefs.MutedUntilUtc);
    }

    [Fact]
    public async Task Update_then_Get_round_trips()
    {
        MockDataStore.NotificationPreferences.Clear();
        var svc = new MockNotificationPreferenceService();

        var prefs = await svc.GetPreferencesAsync("user-1");
        prefs.Matrix["likeReceived"]["telegram"] = true;
        prefs.DailyDigestHourUtc = 20;

        await svc.UpdatePreferencesAsync("user-1", prefs);
        var loaded = await svc.GetPreferencesAsync("user-1");

        Assert.True(loaded.Matrix["likeReceived"]["telegram"]);
        Assert.Equal(20, loaded.DailyDigestHourUtc);
    }

    [Fact]
    public async Task Update_isolates_users()
    {
        MockDataStore.NotificationPreferences.Clear();
        var svc = new MockNotificationPreferenceService();

        var aPrefs = await svc.GetPreferencesAsync("user-a");
        aPrefs.Mute = true;
        await svc.UpdatePreferencesAsync("user-a", aPrefs);

        var bPrefs = await svc.GetPreferencesAsync("user-b");
        Assert.False(bPrefs.Mute);
    }
}
