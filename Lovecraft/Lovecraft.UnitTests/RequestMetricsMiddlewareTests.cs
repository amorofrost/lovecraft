using Lovecraft.Backend.Middleware;
using Lovecraft.Backend.Services.Metrics;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Lovecraft.UnitTests;

public class RequestMetricsMiddlewareTests
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/api/v1/metrics/config")]
    [InlineData("/swagger/index.html")]
    public async Task SkippedPaths_DoNotRecord(string path)
    {
        var collector = new MockMetricsCollector();
        var dau = new DailyActiveUserCoalescer();
        var mw = new RequestMetricsMiddleware(_ => Task.CompletedTask, collector, dau);
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = path;
        await mw.InvokeAsync(ctx);
        Assert.Empty(collector.Snapshot());
    }

    [Fact]
    public async Task OptionsMethod_NotRecorded()
    {
        var collector = new MockMetricsCollector();
        var mw = new RequestMetricsMiddleware(_ => Task.CompletedTask, collector, new DailyActiveUserCoalescer());
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "OPTIONS";
        ctx.Request.Path = "/api/v1/users";
        await mw.InvokeAsync(ctx);
        Assert.Empty(collector.Snapshot());
    }

    [Fact]
    public async Task NormalRequest_RecordsTiming()
    {
        var collector = new MockMetricsCollector();
        var mw = new RequestMetricsMiddleware(c => { c.Response.StatusCode = 200; return Task.CompletedTask; },
                                              collector, new DailyActiveUserCoalescer());
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/v1/auth/login";
        await mw.InvokeAsync(ctx);
        Assert.Single(collector.Snapshot());
        var row = collector.Snapshot()[0];
        Assert.Equal("request_timing", row.Category);
        Assert.Contains("backend|POST|api~v1~auth~login|200", row.DimensionKey);
    }

    [Fact]
    public async Task MiddlewareNeverThrows_EvenIfCollectorThrows()
    {
        var bad = new ThrowingCollector();
        var mw = new RequestMetricsMiddleware(_ => Task.CompletedTask, bad, new DailyActiveUserCoalescer());
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/api/v1/users";
        await mw.InvokeAsync(ctx);  // should not throw
    }

    private sealed class ThrowingCollector : IMetricsCollector
    {
        public MetricsEnabledFlags CurrentFlags { get; } = MetricsEnabledFlags.AllEnabled;
        public void UpdateFlags(MetricsEnabledFlags f) { }
        public void RecordTiming(string c, string d, double m) => throw new InvalidOperationException();
        public void RecordCount(string c, string d, long delta = 1) => throw new InvalidOperationException();
        public Task RecordContainerStatusAsync(ContainerStatusSnapshot s, CancellationToken ct = default) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
