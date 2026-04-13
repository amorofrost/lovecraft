using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lovecraft.UnitTests;

public class RateLimitingTests
{
    // Each test creates its own factory so rate-limiter in-memory state is isolated.

    [Fact]
    public async Task Login_ExceedingRateLimit_Returns429()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        HttpResponseMessage? last = null;
        for (int i = 0; i <= 5; i++) // 6 requests — one over the 5-per-15-min limit
        {
            last = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "attacker@evil.com", password = "wrong" });
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }

    [Fact]
    public async Task Login_WithinRateLimit_DoesNotReturn429()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        HttpResponseMessage? last = null;
        for (int i = 0; i < 5; i++) // exactly 5 — at the limit, not over
        {
            last = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "attacker@evil.com", password = "wrong" });
        }

        Assert.NotEqual(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }

    [Fact]
    public async Task Register_ExceedingRateLimit_Returns429()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        HttpResponseMessage? last = null;
        for (int i = 0; i <= 5; i++)
        {
            last = await client.PostAsJsonAsync("/api/v1/auth/register",
                new { email = $"user{i}@evil.com", password = "wrong", name = "Evil" });
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_ExceedingRateLimit_Returns429()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        HttpResponseMessage? last = null;
        for (int i = 0; i <= 5; i++)
        {
            last = await client.PostAsJsonAsync("/api/v1/auth/forgot-password",
                new { email = "victim@example.com" });
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }

    [Fact]
    public async Task RateLimitResponse_HasCorrectErrorCode()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        // Send 6 requests to exhaust the limit (6th is first rejection, discarded).
        // Assert the body shape on the 7th, which is also rejected.
        for (int i = 0; i < 6; i++)
            await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = "x@x.com", password = "wrong" });

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "x@x.com", password = "wrong" });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.False(body!.Success);
        Assert.Equal("TOO_MANY_REQUESTS", body.Error?.Code);
    }

    // Minimal shape to deserialise the ApiResponse error without pulling in the full DTO
    private sealed record ErrorBody(bool Success, ErrorDetail? Error);
    private sealed record ErrorDetail(string Code, string Message);
}
