using System.Net;
using System.Text.Json;
using Lovecraft.TelegramBot;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lovecraft.UnitTests.TelegramBot;

public class ContainerMetricsReporterTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task ReportAsync_PostsSamplesWithServiceToken()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://backend:8080") };
        var reporter = new ContainerMetricsReporter(http, "tok-123", NullLogger<ContainerMetricsReporter>.Instance);

        await reporter.ReportAsync("telegram-bot", gcHeapMb: 22, workingSetMb: 98, threadCount: 14, cpuPercent: 3.5, default);

        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("/api/v1/internal/metrics/container-stats", handler.Request.RequestUri!.AbsolutePath);
        Assert.True(handler.Request.Headers.TryGetValues("X-Service-Token", out var tok));
        Assert.Equal("tok-123", System.Linq.Enumerable.First(tok!));

        using var doc = JsonDocument.Parse(handler.Body!);
        Assert.Equal("telegram-bot", doc.RootElement.GetProperty("container").GetString());
        Assert.Equal(22, doc.RootElement.GetProperty("gcHeapMb").GetInt64());
        Assert.Equal(3.5, doc.RootElement.GetProperty("cpuPercent").GetDouble(), 3);
    }

    [Fact]
    public async Task ReportAsync_SwallowsHttpFailure()
    {
        var http = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://backend:8080") };
        var reporter = new ContainerMetricsReporter(http, "tok", NullLogger<ContainerMetricsReporter>.Instance);
        await reporter.ReportAsync("x", 1, 1, 1, 1, default);  // must not throw
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("boom");
    }

    [Fact]
    public async Task ReportAsync_SwallowsNonSuccessStatus()
    {
        var http = new HttpClient(new StatusHandler(System.Net.HttpStatusCode.InternalServerError))
            { BaseAddress = new Uri("http://backend:8080") };
        var reporter = new ContainerMetricsReporter(http, "tok", NullLogger<ContainerMetricsReporter>.Instance);
        await reporter.ReportAsync("x", 1, 1, 1, 1, default);  // must not throw
    }

    private sealed class StatusHandler : HttpMessageHandler
    {
        private readonly System.Net.HttpStatusCode _code;
        public StatusHandler(System.Net.HttpStatusCode code) => _code = code;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(_code));
    }
}
