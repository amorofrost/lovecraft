using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Xunit;

namespace Lovecraft.UnitTests;

public class NotificationPolicyFcmTests
{
    private static NotificationPreferencesDto PrefsWithFcm(bool fcm)
    {
        var p = new NotificationPreferencesDto();
        p.Matrix["matchCreated"] = new() { ["inApp"] = true, ["fcm"] = fcm };
        return p;
    }

    [Fact]
    public void Fcm_Added_WhenEnabled_AndRegistered()
    {
        var channels = NotificationPolicy.ResolveChannels(
            PrefsWithFcm(true), NotificationType.MatchCreated,
            new ChannelAvailability { FcmRegistered = true });
        Assert.Contains(NotificationChannel.Fcm, channels);
    }

    [Fact]
    public void Fcm_NotAdded_WhenNoDeviceRegistered()
    {
        var channels = NotificationPolicy.ResolveChannels(
            PrefsWithFcm(true), NotificationType.MatchCreated,
            new ChannelAvailability { FcmRegistered = false });
        Assert.DoesNotContain(NotificationChannel.Fcm, channels);
    }

    [Fact]
    public void Fcm_NotAdded_WhenDisabledInPrefs()
    {
        var channels = NotificationPolicy.ResolveChannels(
            PrefsWithFcm(false), NotificationType.MatchCreated,
            new ChannelAvailability { FcmRegistered = true });
        Assert.DoesNotContain(NotificationChannel.Fcm, channels);
    }
}
