using System.Text.Json;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

/// <summary>
/// Phase H — Task 1: verify that <see cref="MockUserService.IncrementCounterAsync"/> fires
/// a <see cref="NotificationType.RankUp"/> notification when (and only when) a counter
/// increment promotes the user across a rank threshold.
///
/// Tests use <see cref="MockUserService"/> because the rank-up logic lives in a shared
/// private helper that behaves identically across both Azure and Mock variants — the
/// Mock service avoids Azure Table fakes and is the simplest harness.
/// </summary>
public class UserServiceRankUpTests
{
    private static readonly RankThresholds T = RankThresholds.Defaults;

    /// <summary>
    /// Resets the shared <see cref="MockDataStore"/> state touched by these tests so they
    /// can run independently of the seed data and not bleed into each other.
    /// Assembly-level [CollectionBehavior(DisableTestParallelization = true)] makes the
    /// sequential reset safe.
    /// </summary>
    private static void ResetActivity(string userId)
    {
        MockDataStore.UserActivity.Remove(userId);
        MockDataStore.UserRankOverrides.Remove(userId);
    }

    private static void SeedActivity(string userId, int replies = 0, int likes = 0, int events = 0, int matches = 0)
    {
        MockDataStore.UserActivity[userId] = new MockUserActivity
        {
            ReplyCount = replies,
            LikesReceived = likes,
            EventsAttended = events,
            MatchCount = matches,
        };
    }

    private static MockUserService Build(Mock<INotificationProducer>? producer)
    {
        Lazy<INotificationProducer>? lazy = producer is null
            ? null
            : new Lazy<INotificationProducer>(() => producer.Object);
        return new MockUserService(
            new MockAppConfigService(),
            lazy,
            NullLogger<MockUserService>.Instance);
    }

    [Fact]
    public async Task IncrementCounterAsync_CountersCrossThreshold_FiresRankUp()
    {
        const string userId = "rankup-test-1";
        ResetActivity(userId);
        try
        {
            // ActiveReplies threshold is 5. Seed at 4, increment to 5 → Novice → ActiveMember.
            SeedActivity(userId, replies: T.ActiveReplies - 1);
            var producer = new Mock<INotificationProducer>();
            var svc = Build(producer);

            await svc.IncrementCounterAsync(userId, UserCounter.ReplyCount);

            producer.Verify(p => p.ProduceAsync(
                userId,
                NotificationType.RankUp,
                null,
                It.Is<string>(s => s.Contains("activeMember")),
                $"rank-up-{userId}-activeMember",
                null), Times.Once);
        }
        finally { ResetActivity(userId); }
    }

    [Fact]
    public async Task IncrementCounterAsync_NoRankChange_DoesNotFire()
    {
        const string userId = "rankup-test-2";
        ResetActivity(userId);
        try
        {
            // Novice → Novice. ReplyCount 1 → 2; still below ActiveReplies (5).
            SeedActivity(userId, replies: 1);
            var producer = new Mock<INotificationProducer>();
            var svc = Build(producer);

            await svc.IncrementCounterAsync(userId, UserCounter.ReplyCount);

            producer.Verify(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()), Times.Never);
        }
        finally { ResetActivity(userId); }
    }

    [Fact]
    public async Task IncrementCounterAsync_RankOverrideSet_DoesNotFire()
    {
        const string userId = "rankup-test-3";
        ResetActivity(userId);
        try
        {
            // Even though the counter increment would normally promote the rank,
            // RankCalculator short-circuits on RankOverride — both before and after
            // it yields the overridden rank, so no change fires.
            SeedActivity(userId, replies: T.ActiveReplies - 1);
            MockDataStore.UserRankOverrides[userId] = UserRank.AloeCrew;
            var producer = new Mock<INotificationProducer>();
            var svc = Build(producer);

            await svc.IncrementCounterAsync(userId, UserCounter.ReplyCount);

            producer.Verify(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()), Times.Never);
        }
        finally { ResetActivity(userId); }
    }

    [Fact]
    public async Task IncrementCounterAsync_NegativeDelta_RankDrop_DoesNotFire()
    {
        const string userId = "rankup-test-4";
        ResetActivity(userId);
        try
        {
            // Start at exactly ActiveReplies (ActiveMember), decrement to below threshold (Novice).
            // The transition is a rank DROP — must not fire RankUp.
            SeedActivity(userId, replies: T.ActiveReplies);
            var producer = new Mock<INotificationProducer>();
            var svc = Build(producer);

            await svc.IncrementCounterAsync(userId, UserCounter.ReplyCount, delta: -1);

            producer.Verify(p => p.ProduceAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()), Times.Never);
        }
        finally { ResetActivity(userId); }
    }

    [Fact]
    public async Task IncrementCounterAsync_NullProducer_DoesNotThrow()
    {
        const string userId = "rankup-test-5";
        ResetActivity(userId);
        try
        {
            // No producer injected → service should silently no-op the rank-up path
            // and still apply the counter increment.
            SeedActivity(userId, replies: T.ActiveReplies - 1);
            var svc = Build(producer: null);

            await svc.IncrementCounterAsync(userId, UserCounter.ReplyCount);

            Assert.Equal(T.ActiveReplies, MockDataStore.UserActivity[userId].ReplyCount);
        }
        finally { ResetActivity(userId); }
    }

    [Fact]
    public async Task IncrementCounterAsync_PayloadIncludesPreviousAndNewRank()
    {
        const string userId = "rankup-test-6";
        ResetActivity(userId);
        try
        {
            SeedActivity(userId, replies: T.ActiveReplies - 1);
            var producer = new Mock<INotificationProducer>();
            string? capturedPayload = null;
            producer
                .Setup(p => p.ProduceAsync(
                    It.IsAny<string>(),
                    NotificationType.RankUp,
                    It.IsAny<string?>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .Callback<string, NotificationType, string?, string, string?, string?>(
                    (_, _, _, payload, _, _) => capturedPayload = payload)
                .ReturnsAsync((NotificationDto?)null);

            var svc = Build(producer);

            await svc.IncrementCounterAsync(userId, UserCounter.ReplyCount);

            Assert.NotNull(capturedPayload);
            using var doc = JsonDocument.Parse(capturedPayload!);
            var root = doc.RootElement;
            Assert.True(root.TryGetProperty("previousRank", out var prev));
            Assert.True(root.TryGetProperty("newRank", out var next));
            Assert.Equal("novice", prev.GetString());
            Assert.Equal("activeMember", next.GetString());
        }
        finally { ResetActivity(userId); }
    }
}
