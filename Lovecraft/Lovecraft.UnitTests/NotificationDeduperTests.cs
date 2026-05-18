using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.Enums;
using Xunit;

namespace Lovecraft.UnitTests;

public class NotificationDeduperTests
{
    public NotificationDeduperTests() { MockDataStore.Notifications.Clear(); }

    [Fact]
    public async Task First_call_is_not_duplicate()
    {
        var svc = new MockNotificationService();
        var deduper = new NotificationDeduper(svc);

        var isDup = await deduper.IsDuplicateAsync("u1", NotificationType.MessageReceived, "actor", "msg-1");

        Assert.False(isDup);
    }

    [Fact]
    public async Task Second_call_within_window_is_duplicate()
    {
        var svc = new MockNotificationService();
        var deduper = new NotificationDeduper(svc);
        await svc.CreateAsync("u1", NotificationType.MessageReceived, "actor", "{}", "msg-1");

        var isDup = await deduper.IsDuplicateAsync("u1", NotificationType.MessageReceived, "actor", "msg-1");

        Assert.True(isDup);
    }

    [Fact]
    public async Task Different_source_event_is_not_duplicate()
    {
        var svc = new MockNotificationService();
        var deduper = new NotificationDeduper(svc);
        await svc.CreateAsync("u1", NotificationType.MessageReceived, "actor", "{}", "msg-1");

        var isDup = await deduper.IsDuplicateAsync("u1", NotificationType.MessageReceived, "actor", "msg-2");

        Assert.False(isDup);
    }

    [Fact]
    public async Task Null_source_event_skips_dedup()
    {
        var svc = new MockNotificationService();
        var deduper = new NotificationDeduper(svc);
        await svc.CreateAsync("u1", NotificationType.MatchCreated, "actor", "{}", null);

        // Two MatchCreated with no source event id — caller is responsible for at-most-once;
        // deduper should not block.
        var isDup = await deduper.IsDuplicateAsync("u1", NotificationType.MatchCreated, "actor", null);

        Assert.False(isDup);
    }
}
