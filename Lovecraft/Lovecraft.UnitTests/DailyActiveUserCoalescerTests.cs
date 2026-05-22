using Lovecraft.Backend.Services.Metrics;
using Xunit;

namespace Lovecraft.UnitTests;

public class DailyActiveUserCoalescerTests
{
    [Fact]
    public void ShouldFlush_FirstHit_True()
    {
        var c = new DailyActiveUserCoalescer(windowSeconds: 60);
        Assert.True(c.ShouldFlush("u1", new DateTime(2026, 5, 21, 14, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void ShouldFlush_SecondHitWithinWindow_False()
    {
        var c = new DailyActiveUserCoalescer(windowSeconds: 60);
        var t0 = new DateTime(2026, 5, 21, 14, 0, 0, DateTimeKind.Utc);
        c.ShouldFlush("u1", t0);
        Assert.False(c.ShouldFlush("u1", t0.AddSeconds(30)));
    }

    [Fact]
    public void ShouldFlush_AfterWindow_True()
    {
        var c = new DailyActiveUserCoalescer(windowSeconds: 60);
        var t0 = new DateTime(2026, 5, 21, 14, 0, 0, DateTimeKind.Utc);
        c.ShouldFlush("u1", t0);
        Assert.True(c.ShouldFlush("u1", t0.AddSeconds(61)));
    }

    [Fact]
    public void ShouldFlush_NewDay_True()
    {
        var c = new DailyActiveUserCoalescer(windowSeconds: 60);
        var day1 = new DateTime(2026, 5, 21, 23, 59, 50, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 5, 22, 0, 0, 5, DateTimeKind.Utc);
        c.ShouldFlush("u1", day1);
        Assert.True(c.ShouldFlush("u1", day2));
    }
}
