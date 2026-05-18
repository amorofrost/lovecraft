using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

public class NotificationProducerTests
{
    public NotificationProducerTests()
    {
        MockDataStore.Notifications.Clear();
        MockDataStore.NotificationPreferences.Clear();
        MockDataStore.PushSubscriptions.Clear();
    }

    private static (NotificationProducer Producer, MockNotificationService Notifs, Mock<IInAppDispatcher> InApp)
        BuildProducer(NotificationPreferencesDto? prefs = null, IPresenceTracker? presence = null)
    {
        var notifs = new MockNotificationService();
        var prefSvc = new MockNotificationPreferenceService();
        if (prefs is not null) prefSvc.UpdatePreferencesAsync("u-recipient", prefs).GetAwaiter().GetResult();
        var pushSvc = new MockPushSubscriptionService();
        var userSvc = new Mock<IUserService>();
        userSvc.Setup(s => s.GetNotificationContactStatusAsync("u-recipient"))
            .ReturnsAsync((false, false));   // (TelegramLinked, EmailVerified) — overridden per-test via Setup chaining
        var inApp = new Mock<IInAppDispatcher>();
        var deduper = new NotificationDeduper(notifs);
        var producer = new NotificationProducer(
            notifs, prefSvc, pushSvc, userSvc.Object, inApp.Object,
            presence ?? new PresenceTracker(), deduper,
            NullLogger<NotificationProducer>.Instance);
        return (producer, notifs, inApp);
    }

    [Fact]
    public async Task Produce_writes_notification_row()
    {
        var (producer, notifs, _) = BuildProducer();

        var n = await producer.ProduceAsync("u-recipient", NotificationType.LikeReceived,
            actorId: "u-actor", payloadJson: "{}", sourceEventId: "like-1");

        Assert.NotNull(n);
        var list = await notifs.ListAsync("u-recipient", 10, null);
        Assert.Single(list);
    }

    [Fact]
    public async Task Self_action_is_skipped()
    {
        var (producer, notifs, _) = BuildProducer();

        var n = await producer.ProduceAsync("u-recipient", NotificationType.LikeReceived,
            actorId: "u-recipient", payloadJson: "{}", sourceEventId: "like-2");

        Assert.Null(n);
        Assert.Empty(await notifs.ListAsync("u-recipient", 10, null));
    }

    [Fact]
    public async Task Duplicate_within_window_is_skipped()
    {
        var (producer, notifs, _) = BuildProducer();
        await producer.ProduceAsync("u-recipient", NotificationType.MessageReceived,
            "actor", "{}", "msg-1");

        var second = await producer.ProduceAsync("u-recipient", NotificationType.MessageReceived,
            "actor", "{}", "msg-1");

        Assert.Null(second);
        Assert.Single(await notifs.ListAsync("u-recipient", 10, null));
    }

    [Fact]
    public async Task MessageReceived_skipped_when_recipient_present_in_chat()
    {
        var presence = new PresenceTracker();
        presence.Join("chat-1", "u-recipient");
        var (producer, notifs, inApp) = BuildProducer(presence: presence);

        var payload = System.Text.Json.JsonSerializer.Serialize(new { chatId = "1", messageId = "m1" });
        var n = await producer.ProduceAsync("u-recipient", NotificationType.MessageReceived,
            actorId: "actor", payloadJson: payload, sourceEventId: "m1");

        Assert.Null(n);
        Assert.Empty(await notifs.ListAsync("u-recipient", 10, null));
        inApp.Verify(d => d.DispatchAsync(It.IsAny<string>(), It.IsAny<NotificationDto>()), Times.Never);
    }

    [Fact]
    public async Task InApp_dispatcher_called_when_channels_include_in_app()
    {
        var (producer, _, inApp) = BuildProducer();

        var n = await producer.ProduceAsync("u-recipient", NotificationType.LikeReceived,
            "actor", "{}", "like-3");

        Assert.NotNull(n);
        inApp.Verify(d => d.DispatchAsync("u-recipient",
            It.Is<NotificationDto>(x => x.Id == n!.Id)), Times.Once);
    }
}
