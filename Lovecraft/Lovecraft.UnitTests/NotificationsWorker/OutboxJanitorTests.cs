using Azure;
using Azure.Data.Tables;
using Lovecraft.NotificationsWorker.Entities;
using Lovecraft.NotificationsWorker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests.NotificationsWorker;

public class OutboxJanitorTests
{
    [Fact]
    public async Task Deletes_outbox_rows_in_partitions_older_than_30_days()
    {
        var outbox = new Mock<TableClient>();
        var notifs = new Mock<TableClient>();
        var now = new DateTime(2026, 5, 18, 3, 0, 0, DateTimeKind.Utc);
        var oldDate = now.AddDays(-31);
        var oldRow = new NotificationOutboxEntity
        {
            PartitionKey = NotificationOutboxEntity.DonePartition("Telegram", oldDate),
            RowKey = "rk",
            ETag = new ETag("e"),
            Channel = "Telegram",
        };

        outbox.Setup(t => t.QueryAsync<NotificationOutboxEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { oldRow }.ToAsyncPageable());
        outbox.Setup(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<Response>().Object);

        notifs.Setup(t => t.QueryAsync<NotificationEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Array.Empty<NotificationEntity>().ToAsyncPageable());

        var janitor = new OutboxJanitor(outbox.Object, notifs.Object, NullLogger<OutboxJanitor>.Instance);
        await janitor.RunAsync(now, CancellationToken.None);

        outbox.Verify(t => t.DeleteEntityAsync(
            oldRow.PartitionKey, oldRow.RowKey, It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deletes_notification_rows_older_than_90_days()
    {
        var outbox = new Mock<TableClient>();
        var notifs = new Mock<TableClient>();
        var now = new DateTime(2026, 5, 18, 3, 0, 0, DateTimeKind.Utc);
        var oldNotif = new NotificationEntity
        {
            PartitionKey = "u1",
            RowKey = "rk",
            ETag = new ETag("e"),
            CreatedAtUtc = now.AddDays(-91),
        };

        outbox.Setup(t => t.QueryAsync<NotificationOutboxEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Array.Empty<NotificationOutboxEntity>().ToAsyncPageable());

        notifs.Setup(t => t.QueryAsync<NotificationEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { oldNotif }.ToAsyncPageable());
        notifs.Setup(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<Response>().Object);

        var janitor = new OutboxJanitor(outbox.Object, notifs.Object, NullLogger<OutboxJanitor>.Instance);
        await janitor.RunAsync(now, CancellationToken.None);

        notifs.Verify(t => t.DeleteEntityAsync(
            oldNotif.PartitionKey, oldNotif.RowKey, It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Does_not_delete_recent_rows()
    {
        var outbox = new Mock<TableClient>();
        var notifs = new Mock<TableClient>();
        var now = new DateTime(2026, 5, 18, 3, 0, 0, DateTimeKind.Utc);
        var recentRow = new NotificationOutboxEntity
        {
            PartitionKey = NotificationOutboxEntity.DonePartition("Telegram", now.AddDays(-5)),
            RowKey = "rk",
            ETag = new ETag("e"),
        };

        outbox.Setup(t => t.QueryAsync<NotificationOutboxEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { recentRow }.ToAsyncPageable());
        notifs.Setup(t => t.QueryAsync<NotificationEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Array.Empty<NotificationEntity>().ToAsyncPageable());

        var janitor = new OutboxJanitor(outbox.Object, notifs.Object, NullLogger<OutboxJanitor>.Instance);
        await janitor.RunAsync(now, CancellationToken.None);

        outbox.Verify(t => t.DeleteEntityAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
