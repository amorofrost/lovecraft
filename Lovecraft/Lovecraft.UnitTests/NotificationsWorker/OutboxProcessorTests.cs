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

public class OutboxProcessorTests
{
    private static NotificationOutboxEntity MakeRow(string channel, DateTime scheduledForUtc, int attempts = 0, string nid = "n1") =>
        new()
        {
            PartitionKey = NotificationOutboxEntity.PendingPartition(channel),
            RowKey = NotificationOutboxEntity.GetRowKey(scheduledForUtc, nid),
            NotificationId = nid,
            UserId = "u1",
            Channel = channel,
            Frequency = "Immediate",
            ScheduledForUtc = scheduledForUtc,
            Attempts = attempts,
            ETag = new ETag("etag"),
        };

    private static NotificationEntity MakeNotif(string nid = "n1") => new()
    {
        PartitionKey = NotificationEntity.GetPartitionKey("u1"),
        RowKey = "rk",
        NotificationId = nid,
        UserId = "u1",
        Type = "LikeReceived",
        PayloadJson = "{}",
        CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
    };

    private static (OutboxProcessor processor, Mock<TableClient> outbox, Mock<TableClient> notifs, Mock<ITelegramDispatcher> dispatcher)
        Build(DispatchResult dispatchResult)
    {
        var outbox = new Mock<TableClient>();
        var notifs = new Mock<TableClient>();
        var dispatcher = new Mock<ITelegramDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<NotificationModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dispatchResult);

        // Set up notification lookup (async query)
        var notifEntity = MakeNotif();
        notifs.Setup(t => t.QueryAsync<NotificationEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { notifEntity }.ToAsyncPageable());

        var processor = new OutboxProcessor(
            outbox.Object, notifs.Object,
            dispatcher.Object, Mock.Of<IEmailDispatcher>(),
            NullLogger<OutboxProcessor>.Instance);

        return (processor, outbox, notifs, dispatcher);
    }

    [Fact]
    public async Task Empty_partition_does_nothing()
    {
        var (processor, outbox, _, dispatcher) = Build(DispatchResult.Delivered);
        outbox.Setup(t => t.QueryAsync<NotificationOutboxEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Array.Empty<NotificationOutboxEntity>().ToAsyncPageable());

        await processor.ProcessChannelAsync("Telegram", CancellationToken.None);

        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<NotificationModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Successful_dispatch_moves_row_to_DONE_partition()
    {
        var now = DateTime.UtcNow;
        var (processor, outbox, _, dispatcher) = Build(DispatchResult.Delivered);
        outbox.Setup(t => t.QueryAsync<NotificationOutboxEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { MakeRow("Telegram", now.AddMinutes(-1)) }.ToAsyncPageable());

        NotificationOutboxEntity? doneRow = null;
        outbox.Setup(t => t.AddEntityAsync(It.IsAny<NotificationOutboxEntity>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationOutboxEntity, CancellationToken>((e, _) => doneRow = e)
            .ReturnsAsync(new Mock<Response>().Object);
        outbox.Setup(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<Response>().Object);

        await processor.ProcessChannelAsync("Telegram", CancellationToken.None);

        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<NotificationModel>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(doneRow);
        Assert.StartsWith("OUTBOX_Telegram_DONE_", doneRow!.PartitionKey);
        Assert.NotNull(doneRow.DeliveredAtUtc);
    }

    [Fact]
    public async Task Retryable_error_reschedules_with_backoff()
    {
        var now = DateTime.UtcNow;
        var (processor, outbox, _, _) = Build(DispatchResult.RetryableError);
        outbox.Setup(t => t.QueryAsync<NotificationOutboxEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { MakeRow("Telegram", now.AddMinutes(-1), attempts: 0) }.ToAsyncPageable());

        NotificationOutboxEntity? rescheduledRow = null;
        outbox.Setup(t => t.AddEntityAsync(It.IsAny<NotificationOutboxEntity>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationOutboxEntity, CancellationToken>((e, _) => rescheduledRow = e)
            .ReturnsAsync(new Mock<Response>().Object);
        outbox.Setup(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<Response>().Object);

        await processor.ProcessChannelAsync("Telegram", CancellationToken.None);

        Assert.NotNull(rescheduledRow);
        Assert.StartsWith("OUTBOX_Telegram_PENDING", rescheduledRow!.PartitionKey);
        Assert.Equal(1, rescheduledRow.Attempts);
        Assert.True(rescheduledRow.ScheduledForUtc >= now.AddSeconds(29) && rescheduledRow.ScheduledForUtc <= now.AddSeconds(31),
            $"Expected ~30s backoff, got {rescheduledRow.ScheduledForUtc - now}");
    }

    [Fact]
    public async Task After_5_retryable_failures_row_moves_to_DEAD()
    {
        var now = DateTime.UtcNow;
        var (processor, outbox, _, _) = Build(DispatchResult.RetryableError);
        outbox.Setup(t => t.QueryAsync<NotificationOutboxEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { MakeRow("Telegram", now.AddMinutes(-1), attempts: 4) }.ToAsyncPageable());

        NotificationOutboxEntity? written = null;
        outbox.Setup(t => t.AddEntityAsync(It.IsAny<NotificationOutboxEntity>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationOutboxEntity, CancellationToken>((e, _) => written = e)
            .ReturnsAsync(new Mock<Response>().Object);
        outbox.Setup(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<Response>().Object);

        await processor.ProcessChannelAsync("Telegram", CancellationToken.None);

        Assert.NotNull(written);
        Assert.StartsWith("OUTBOX_Telegram_DEAD_", written!.PartitionKey);
        Assert.Equal(5, written.Attempts);
    }

    [Fact]
    public async Task Permanent_error_moves_to_DEAD_immediately()
    {
        var now = DateTime.UtcNow;
        var (processor, outbox, _, _) = Build(DispatchResult.PermanentError);
        outbox.Setup(t => t.QueryAsync<NotificationOutboxEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { MakeRow("Telegram", now.AddMinutes(-1), attempts: 0) }.ToAsyncPageable());

        NotificationOutboxEntity? written = null;
        outbox.Setup(t => t.AddEntityAsync(It.IsAny<NotificationOutboxEntity>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationOutboxEntity, CancellationToken>((e, _) => written = e)
            .ReturnsAsync(new Mock<Response>().Object);
        outbox.Setup(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mock<Response>().Object);

        await processor.ProcessChannelAsync("Telegram", CancellationToken.None);

        Assert.NotNull(written);
        Assert.StartsWith("OUTBOX_Telegram_DEAD_", written!.PartitionKey);
    }

    [Fact]
    public async Task Future_scheduled_rows_are_not_processed_via_RowKey_filter()
    {
        // OData filter `RowKey le '{now}'` excludes future rows — verify the filter string is correctly formed.
        var (processor, outbox, _, dispatcher) = Build(DispatchResult.Delivered);
        outbox.Setup(t => t.QueryAsync<NotificationOutboxEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Array.Empty<NotificationOutboxEntity>().ToAsyncPageable());

        await processor.ProcessChannelAsync("Telegram", CancellationToken.None);

        outbox.Verify(t => t.QueryAsync<NotificationOutboxEntity>(
            It.Is<string>(f => f.Contains("RowKey le") || f.Contains("RowKey lt")),
            It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

// Helper to convert an enumerable to AsyncPageable<T> for Moq:
internal static class AsyncPageableExtensions
{
    public static AsyncPageable<T> ToAsyncPageable<T>(this IEnumerable<T> items) where T : notnull
    {
        var page = Page<T>.FromValues(items.ToList(), null, new Mock<Response>().Object);
        return AsyncPageable<T>.FromPages(new[] { page });
    }
}
