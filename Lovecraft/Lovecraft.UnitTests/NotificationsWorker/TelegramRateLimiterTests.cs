using Lovecraft.NotificationsWorker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lovecraft.UnitTests.NotificationsWorker;

public class TelegramRateLimiterTests
{
    [Fact]
    public async Task First_call_passes_immediately()
    {
        var limiter = new TelegramRateLimiter(NullLogger<TelegramRateLimiter>.Instance);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.AcquireAsync("chat-1", CancellationToken.None);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 200, $"First call should be immediate, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Second_call_same_chat_within_1s_is_delayed()
    {
        var limiter = new TelegramRateLimiter(NullLogger<TelegramRateLimiter>.Instance);
        await limiter.AcquireAsync("chat-1", CancellationToken.None);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.AcquireAsync("chat-1", CancellationToken.None);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds >= 900, $"Second call should wait ~1s, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Different_chats_have_independent_cooldowns()
    {
        var limiter = new TelegramRateLimiter(NullLogger<TelegramRateLimiter>.Instance);
        await limiter.AcquireAsync("chat-1", CancellationToken.None);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.AcquireAsync("chat-2", CancellationToken.None);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 200, $"Different chat should be immediate, took {sw.ElapsedMilliseconds}ms");
    }
}
