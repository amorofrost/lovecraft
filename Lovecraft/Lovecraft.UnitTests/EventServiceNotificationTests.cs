using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Azure;
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Admin;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

/// <summary>
/// Phase G — Task 4: verify that creating a public event fires <see cref="NotificationType.EventPublished"/>
/// notifications to every user via the injected <see cref="INotificationProducer"/>, that secret events
/// skip the fan-out, and that the optional producer ctor param keeps the service backward-compatible
/// when no producer is wired in (defensive null-safety).
/// </summary>
public class EventServiceNotificationTests
{
    private static MockEventService BuildMockService(
        Mock<INotificationProducer>? producer,
        params UserDto[] users)
    {
        var userService = new Mock<IUserService>();
        userService.Setup(u => u.GetUsersAsync(0, 10_000, null, null))
            .ReturnsAsync(users.ToList());
        return new MockEventService(
            userService.Object,
            producer?.Object,
            NullLogger<MockEventService>.Instance);
    }

    private static AdminEventWriteDto NewEventDto(EventVisibility visibility, string title = "New Event")
        => new AdminEventWriteDto
        {
            Title = title,
            Description = "desc",
            ImageUrl = "img",
            Date = DateTime.UtcNow.AddDays(7),
            Location = "Earth",
            Category = EventCategory.Other,
            Organizer = "org",
            Visibility = visibility,
        };

    [Fact]
    public async Task CreateEventAsync_PublicEvent_FiresEventPublishedForAllUsers()
    {
        // We snapshot Events so the test does not leak into other tests that read MockDataStore.Events.
        var beforeIds = MockDataStore.Events.Select(e => e.Id).ToHashSet();
        try
        {
            var producer = new Mock<INotificationProducer>();
            var svc = BuildMockService(producer,
                new UserDto { Id = "u1", Name = "User One" },
                new UserDto { Id = "u2", Name = "User Two" });

            var dto = await svc.CreateEventAsync(NewEventDto(EventVisibility.Public, "Spring Mixer"));

            producer.Verify(p => p.ProduceAsync(
                "u1",
                NotificationType.EventPublished,
                null,
                It.Is<string>(s => s.Contains("Spring Mixer")),
                $"event-published-{dto.Id}",
                null), Times.Once);

            producer.Verify(p => p.ProduceAsync(
                "u2",
                NotificationType.EventPublished,
                null,
                It.Is<string>(s => s.Contains("Spring Mixer")),
                $"event-published-{dto.Id}",
                null), Times.Once);
        }
        finally
        {
            MockDataStore.Events.RemoveAll(e => !beforeIds.Contains(e.Id));
        }
    }

    [Fact]
    public async Task CreateEventAsync_SecretEvent_DoesNotFireProducer()
    {
        var beforeIds = MockDataStore.Events.Select(e => e.Id).ToHashSet();
        try
        {
            var producer = new Mock<INotificationProducer>();
            var svc = BuildMockService(producer,
                new UserDto { Id = "u1", Name = "User One" });

            await svc.CreateEventAsync(NewEventDto(EventVisibility.SecretHidden, "Hidden Soiree"));

            producer.Verify(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()), Times.Never);
        }
        finally
        {
            MockDataStore.Events.RemoveAll(e => !beforeIds.Contains(e.Id));
        }
    }

    [Fact]
    public async Task CreateEventAsync_NullProducer_DoesNotThrow()
    {
        var beforeIds = MockDataStore.Events.Select(e => e.Id).ToHashSet();
        try
        {
            // No producer wired — service must still create the event and not blow up
            // on the optional null param. This guards the backward-compat ctor.
            var svc = BuildMockService(producer: null);

            var dto = await svc.CreateEventAsync(NewEventDto(EventVisibility.Public, "No-Producer Event"));

            Assert.NotNull(dto);
            Assert.Equal("No-Producer Event", dto.Title);
        }
        finally
        {
            MockDataStore.Events.RemoveAll(e => !beforeIds.Contains(e.Id));
        }
    }

    // ── AzureEventService variant — verifies the wiring on the Azure implementation too ──

    private static AzureEventService BuildAzureService(
        Mock<IUserService> userService,
        Mock<INotificationProducer>? producer)
    {
        var eventsTable = new Mock<TableClient>();
        var attendeesTable = new Mock<TableClient>();
        var interestedTable = new Mock<TableClient>();

        var emptyTableItem = Response.FromValue<TableItem>(null!, Mock.Of<Response>());
        eventsTable.Setup(t => t.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyTableItem);
        attendeesTable.Setup(t => t.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyTableItem);
        interestedTable.Setup(t => t.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyTableItem);

        eventsTable.Setup(t => t.AddEntityAsync(
                It.IsAny<EventEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        var tsc = new Mock<TableServiceClient>();
        tsc.Setup(x => x.GetTableClient(TableNames.Events)).Returns(eventsTable.Object);
        tsc.Setup(x => x.GetTableClient(TableNames.EventAttendees)).Returns(attendeesTable.Object);
        tsc.Setup(x => x.GetTableClient(TableNames.EventInterested)).Returns(interestedTable.Object);

        return new AzureEventService(
            tsc.Object,
            userService.Object,
            NullLogger<AzureEventService>.Instance,
            producer?.Object);
    }

    [Fact]
    public async Task Azure_CreateEventAsync_PublicEvent_FiresProducer()
    {
        var producer = new Mock<INotificationProducer>();
        var userService = new Mock<IUserService>();
        userService.Setup(u => u.GetUsersAsync(0, 10_000, null, null))
            .ReturnsAsync(new List<UserDto>
            {
                new() { Id = "u1", Name = "One" },
                new() { Id = "u2", Name = "Two" },
            });

        var svc = BuildAzureService(userService, producer);
        var dto = await svc.CreateEventAsync(NewEventDto(EventVisibility.Public, "Azure Public Event"));

        producer.Verify(p => p.ProduceAsync(
            "u1",
            NotificationType.EventPublished,
            null,
            It.Is<string>(s => s.Contains("Azure Public Event")),
            $"event-published-{dto.Id}",
            null), Times.Once);
        producer.Verify(p => p.ProduceAsync(
            "u2",
            NotificationType.EventPublished,
            null,
            It.Is<string>(s => s.Contains("Azure Public Event")),
            $"event-published-{dto.Id}",
            null), Times.Once);
    }

    [Fact]
    public async Task Azure_CreateEventAsync_SecretEvent_DoesNotFireProducer()
    {
        var producer = new Mock<INotificationProducer>();
        var userService = new Mock<IUserService>();
        userService.Setup(u => u.GetUsersAsync(0, 10_000, null, null))
            .ReturnsAsync(new List<UserDto> { new() { Id = "u1" } });

        var svc = BuildAzureService(userService, producer);
        await svc.CreateEventAsync(NewEventDto(EventVisibility.SecretTeaser, "Teaser-Only"));

        producer.Verify(p => p.ProduceAsync(
            It.IsAny<string>(),
            It.IsAny<NotificationType>(),
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>()), Times.Never);
    }
}
