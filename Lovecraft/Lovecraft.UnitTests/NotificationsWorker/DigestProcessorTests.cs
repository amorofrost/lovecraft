using Azure;
using Azure.Data.Tables;
using Lovecraft.NotificationsWorker.Dispatchers;
using Lovecraft.NotificationsWorker.Entities;
using Lovecraft.NotificationsWorker.Models;
using Lovecraft.NotificationsWorker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests.NotificationsWorker;

public class DigestProcessorTests
{
    private static NotificationOutboxEntity MakeRow(string channel, string userId, string nid, string frequency = "Hourly") =>
        new()
        {
            PartitionKey = NotificationOutboxEntity.PendingPartition(channel),
            RowKey = NotificationOutboxEntity.GetRowKey(DateTime.UtcNow.AddMinutes(-1), nid),
            NotificationId = nid,
            UserId = userId,
            Channel = channel,
            Frequency = frequency,
            ScheduledForUtc = DateTime.UtcNow.AddMinutes(-1),
            ETag = new ETag("e"),
        };

    private static NotificationPreferencesEntity MakePrefs(string userId, int dailyHourUtc = 9) => new()
    {
        PartitionKey = userId,
        RowKey = "INDEX",
        DailyDigestHourUtc = dailyHourUtc,
    };

    [Fact]
    public async Task Hourly_rows_dispatched_every_hour()
    {
        var outbox = new Mock<TableClient>();
        var notifs = new Mock<TableClient>();
        var prefs = new Mock<TableClient>();
        var telegram = new Mock<ITelegramDispatcher>();
        telegram.Setup(d => d.DispatchAsync(It.IsAny<NotificationModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DispatchResult.Delivered);

        outbox.Setup(t => t.QueryAsync<NotificationOutboxEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { MakeRow("Telegram", "u1", "n1", "Hourly") }.ToAsyncPageable());

        // Notifications lookup — one entity per LoadAsync
        notifs.Setup(t => t.QueryAsync<NotificationEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { new NotificationEntity { NotificationId = "n1", UserId = "u1", Type = "LikeReceived", PayloadJson = "{}", CreatedAtUtc = DateTime.UtcNow } }.ToAsyncPageable());

        // Preferences lookup
        prefs.Setup(t => t.GetEntityAsync<NotificationPreferencesEntity>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(MakePrefs("u1"), new Mock<Response>().Object));

        outbox.Setup(t => t.AddEntityAsync(It.IsAny<NotificationOutboxEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<Response>().Object);
        outbox.Setup(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<Response>().Object);

        var processor = new DigestProcessor(outbox.Object, notifs.Object, prefs.Object,
            telegram.Object, Mock.Of<IEmailDispatcher>(), NullLogger<DigestProcessor>.Instance);

        await processor.ProcessAsync(new DateTime(2026, 5, 18, 14, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        telegram.Verify(d => d.DispatchAsync(It.IsAny<NotificationModel>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Daily_rows_dispatched_only_on_user_hour()
    {
        var outbox = new Mock<TableClient>();
        var notifs = new Mock<TableClient>();
        var prefs = new Mock<TableClient>();
        var email = new Mock<IEmailDispatcher>();
        email.Setup(d => d.DispatchAsync(It.IsAny<NotificationModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DispatchResult.Delivered);

        outbox.Setup(t => t.QueryAsync<NotificationOutboxEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { MakeRow("Email", "u1", "n1", "Daily") }.ToAsyncPageable());

        notifs.Setup(t => t.QueryAsync<NotificationEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { new NotificationEntity { NotificationId = "n1", UserId = "u1", Type = "LikeReceived", PayloadJson = "{}", CreatedAtUtc = DateTime.UtcNow } }.ToAsyncPageable());

        prefs.Setup(t => t.GetEntityAsync<NotificationPreferencesEntity>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(MakePrefs("u1", dailyHourUtc: 9), new Mock<Response>().Object));

        var processor = new DigestProcessor(outbox.Object, notifs.Object, prefs.Object,
            Mock.Of<ITelegramDispatcher>(), email.Object, NullLogger<DigestProcessor>.Instance);

        // At 8 UTC → no dispatch
        await processor.ProcessAsync(new DateTime(2026, 5, 18, 8, 0, 0, DateTimeKind.Utc), CancellationToken.None);
        email.Verify(d => d.DispatchAsync(It.IsAny<NotificationModel>(), It.IsAny<CancellationToken>()), Times.Never);

        outbox.Setup(t => t.AddEntityAsync(It.IsAny<NotificationOutboxEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<Response>().Object);
        outbox.Setup(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<Response>().Object);

        // At 9 UTC → dispatch
        await processor.ProcessAsync(new DateTime(2026, 5, 18, 9, 0, 0, DateTimeKind.Utc), CancellationToken.None);
        email.Verify(d => d.DispatchAsync(It.IsAny<NotificationModel>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Multiple_rows_per_user_grouped_into_one_dispatch()
    {
        var outbox = new Mock<TableClient>();
        var notifs = new Mock<TableClient>();
        var prefs = new Mock<TableClient>();
        var telegram = new Mock<ITelegramDispatcher>();
        telegram.Setup(d => d.DispatchAsync(It.IsAny<NotificationModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DispatchResult.Delivered);

        outbox.Setup(t => t.QueryAsync<NotificationOutboxEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new[]
            {
                MakeRow("Telegram", "u1", "n1", "Hourly"),
                MakeRow("Telegram", "u1", "n2", "Hourly"),
                MakeRow("Telegram", "u1", "n3", "Hourly"),
            }.ToAsyncPageable());

        notifs.Setup(t => t.QueryAsync<NotificationEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns<string, int?, IEnumerable<string>, CancellationToken>((filter, _, _, _) =>
            {
                // Return a notif matching the requested id (extract from filter)
                var nid = filter.Split("NotificationId eq '")[1].TrimEnd('\'');
                return new[] { new NotificationEntity { NotificationId = nid, UserId = "u1", Type = "LikeReceived", PayloadJson = "{}", CreatedAtUtc = DateTime.UtcNow } }.ToAsyncPageable();
            });

        prefs.Setup(t => t.GetEntityAsync<NotificationPreferencesEntity>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(MakePrefs("u1"), new Mock<Response>().Object));

        outbox.Setup(t => t.AddEntityAsync(It.IsAny<NotificationOutboxEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<Response>().Object);
        outbox.Setup(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<Response>().Object);

        var processor = new DigestProcessor(outbox.Object, notifs.Object, prefs.Object,
            telegram.Object, Mock.Of<IEmailDispatcher>(), NullLogger<DigestProcessor>.Instance);

        await processor.ProcessAsync(new DateTime(2026, 5, 18, 14, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        // One dispatch per user, even with 3 pending rows
        telegram.Verify(d => d.DispatchAsync(It.IsAny<NotificationModel>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Empty_partition_does_nothing()
    {
        var outbox = new Mock<TableClient>();
        var notifs = new Mock<TableClient>();
        var prefs = new Mock<TableClient>();
        var telegram = new Mock<ITelegramDispatcher>();

        outbox.Setup(t => t.QueryAsync<NotificationOutboxEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Array.Empty<NotificationOutboxEntity>().ToAsyncPageable());

        var processor = new DigestProcessor(outbox.Object, notifs.Object, prefs.Object,
            telegram.Object, Mock.Of<IEmailDispatcher>(), NullLogger<DigestProcessor>.Instance);

        await processor.ProcessAsync(DateTime.UtcNow, CancellationToken.None);

        telegram.Verify(d => d.DispatchAsync(It.IsAny<NotificationModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
