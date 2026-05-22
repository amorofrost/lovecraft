using Lovecraft.NotificationsWorker.Workers;
using Xunit;

namespace Lovecraft.UnitTests;

public class MetricsRetentionTests
{
    [Fact]
    public void ShouldDeleteMinutePartition_OlderThanRetention_True()
    {
        var now = new DateTime(2026, 5, 21, 12, 0, 0, DateTimeKind.Utc);
        // 2026-05-20T11 is 25 hours before now — older than 24h retention
        Assert.True(JanitorWorker.ShouldDeleteMinutePartition("2026-05-20T11#request_timing", now, retentionHours: 24));
    }

    [Fact]
    public void ShouldDeleteMinutePartition_WithinRetention_False()
    {
        var now = new DateTime(2026, 5, 21, 12, 0, 0, DateTimeKind.Utc);
        // 2026-05-21T01 is 11 hours before now — within 24h retention
        Assert.False(JanitorWorker.ShouldDeleteMinutePartition("2026-05-21T01#request_timing", now, retentionHours: 24));
    }

    [Fact]
    public void ShouldDeleteHourPartition_OlderThanRetention_True()
    {
        var now = new DateTime(2026, 5, 21, 12, 0, 0, DateTimeKind.Utc);
        // 2026-02-15 is >90 days before 2026-05-21 — older than 90d retention
        Assert.True(JanitorWorker.ShouldDeleteHourPartition("2026-02-15#request_timing", now, retentionDays: 90));
    }

    [Fact]
    public void ShouldDeleteDauPartition_OlderThanRetentionPlus1_True()
    {
        var now = new DateTime(2026, 5, 21, 12, 0, 0, DateTimeKind.Utc);
        // retentionDays=30 → cutoff = 2026-05-21 - 31d = 2026-04-20
        // 2026-04-19 < 2026-04-20 → should delete
        Assert.True(JanitorWorker.ShouldDeleteDauPartition("2026-04-19", now, retentionDays: 30));
    }
}
