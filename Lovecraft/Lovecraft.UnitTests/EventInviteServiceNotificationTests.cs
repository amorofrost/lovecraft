using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

/// <summary>
/// Phase G — Task 5: per-user invites + EventInviteReceived producer.
/// Verifies that <see cref="IEventInviteService.IssuePersonalInviteAsync"/> fires the producer
/// while the existing <see cref="IEventInviteService.CreateOrRotateInviteAsync"/> path stays
/// silent (event-level invites do not notify). Producer wiring is via optional constructor
/// params so existing call sites and unit tests continue to compile.
/// </summary>
public class EventInviteServiceNotificationTests
{
    [Fact]
    public async Task IssuePersonalInviteAsync_FiresEventInviteReceived()
    {
        var producer = new Mock<INotificationProducer>();
        var events = new Mock<IEventService>();
        events.Setup(e => e.GetEventByIdAdminAsync("evt-1"))
            .ReturnsAsync(new EventDto { Id = "evt-1", Title = "Test Show" });
        var svc = new MockEventInviteService(
            producer: producer.Object,
            events: events.Object,
            logger: NullLogger<MockEventInviteService>.Instance);

        var (plain, _) = await svc.IssuePersonalInviteAsync(
            eventId: "evt-1",
            targetUserId: "user-42",
            expiresAtUtc: null,
            issuedByUserId: "admin-1");

        Assert.False(string.IsNullOrEmpty(plain));
        producer.Verify(p => p.ProduceAsync(
            "user-42",
            NotificationType.EventInviteReceived,
            "admin-1",
            It.Is<string>(s => s.Contains(plain) && s.Contains("evt-1") && s.Contains("Test Show")),
            "event-invite-evt-1-user-42",
            null), Times.Once);
    }

    [Fact]
    public async Task CreateOrRotateInviteAsync_DoesNotFireProducer()
    {
        var producer = new Mock<INotificationProducer>();
        var svc = new MockEventInviteService(producer: producer.Object);

        await svc.CreateOrRotateInviteAsync("evt-rotate-1", DateTime.UtcNow.AddDays(7));

        producer.Verify(p => p.ProduceAsync(
            It.IsAny<string>(),
            It.IsAny<NotificationType>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task IssuePersonalInviteAsync_NullProducer_DoesNotThrow()
    {
        // No producer wired — service must still create the invite and not blow up.
        var svc = new MockEventInviteService();

        var (plain, _) = await svc.IssuePersonalInviteAsync(
            eventId: "evt-noprod",
            targetUserId: "user-1",
            expiresAtUtc: null,
            issuedByUserId: "admin-1");

        Assert.False(string.IsNullOrEmpty(plain));
        // Code is redeemable regardless of TargetUserId (informational metadata only)
        var validated = await svc.ValidatePlainCodeAsync(plain);
        Assert.NotNull(validated);
        Assert.Equal("evt-noprod", validated!.EventId);
    }

    [Fact]
    public async Task IssuePersonalInviteAsync_WithPlainCodeOverride_UsesProvidedCode()
    {
        var svc = new MockEventInviteService();
        var (plain, _) = await svc.IssuePersonalInviteAsync(
            eventId: "evt-custom",
            targetUserId: "user-1",
            expiresAtUtc: null,
            issuedByUserId: "admin-1",
            plainCodeOverride: "CUSTOM123");
        Assert.Equal("CUSTOM123", plain);
    }

    [Fact]
    public async Task IssuePersonalInviteAsync_FallsBackToDefaultTitle_WhenEventLookupNull()
    {
        var producer = new Mock<INotificationProducer>();
        var events = new Mock<IEventService>();
        events.Setup(e => e.GetEventByIdAdminAsync(It.IsAny<string>()))
            .ReturnsAsync((EventDto?)null);
        var svc = new MockEventInviteService(producer: producer.Object, events: events.Object);

        await svc.IssuePersonalInviteAsync("evt-missing", "user-9", null, "admin-1");

        producer.Verify(p => p.ProduceAsync(
            "user-9",
            NotificationType.EventInviteReceived,
            "admin-1",
            It.Is<string>(s => s.Contains("evt-missing")),
            "event-invite-evt-missing-user-9",
            null), Times.Once);
    }

    [Fact]
    public async Task IssuePersonalInviteAsync_ProducerException_DoesNotBreakInviteCreation()
    {
        var producer = new Mock<INotificationProducer>();
        producer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var svc = new MockEventInviteService(producer: producer.Object);

        var (plain, _) = await svc.IssuePersonalInviteAsync(
            "evt-throw", "user-1", null, "admin-1");

        Assert.False(string.IsNullOrEmpty(plain));
        // Invite is still queryable
        var validated = await svc.ValidatePlainCodeAsync(plain);
        Assert.NotNull(validated);
    }

    [Fact]
    public async Task IssuePersonalInviteAsync_RejectsCampaignEventId()
    {
        var svc = new MockEventInviteService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.IssuePersonalInviteAsync("-1", "user-1", null, "admin-1"));
    }

    [Fact]
    public async Task IssuePersonalInviteAsync_RejectsMissingTargetUserId()
    {
        var svc = new MockEventInviteService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.IssuePersonalInviteAsync("evt-1", "  ", null, "admin-1"));
    }
}
