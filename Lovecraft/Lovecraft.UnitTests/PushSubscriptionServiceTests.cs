using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Azure;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

public class MockPushSubscriptionServiceTests
{
    public MockPushSubscriptionServiceTests() { MockDataStore.PushSubscriptions.Clear(); }

    [Fact]
    public async Task Subscribe_assigns_deviceId_when_missing()
    {
        var svc = new MockPushSubscriptionService();
        var sub = await svc.SubscribeAsync("u1", new WebPushSubscriptionRequestDto
        {
            Endpoint = "https://push.example/abc", P256dh = "p", Auth = "a", UserAgent = "Test"
        });

        Assert.False(string.IsNullOrEmpty(sub.DeviceId));
        Assert.Equal("https://push.example/abc", sub.Endpoint);
    }

    [Fact]
    public async Task Subscribe_with_existing_deviceId_updates_LastSeen()
    {
        var svc = new MockPushSubscriptionService();
        var first = await svc.SubscribeAsync("u1", new WebPushSubscriptionRequestDto
        {
            DeviceId = "fixed", Endpoint = "https://push.example/v1", P256dh = "p", Auth = "a", UserAgent = "v1"
        });
        await Task.Delay(10);
        var second = await svc.SubscribeAsync("u1", new WebPushSubscriptionRequestDto
        {
            DeviceId = "fixed", Endpoint = "https://push.example/v2", P256dh = "p", Auth = "a", UserAgent = "v2"
        });

        Assert.Equal("fixed", second.DeviceId);
        Assert.Equal("https://push.example/v2", second.Endpoint);
        Assert.True(second.LastSeenAtUtc > first.LastSeenAtUtc);
        Assert.Equal(1, await svc.CountAsync("u1"));
    }

    [Fact]
    public async Task List_returns_only_user_rows()
    {
        var svc = new MockPushSubscriptionService();
        await svc.SubscribeAsync("u1", new WebPushSubscriptionRequestDto { Endpoint = "1", P256dh = "p", Auth = "a", UserAgent = "" });
        await svc.SubscribeAsync("u2", new WebPushSubscriptionRequestDto { Endpoint = "2", P256dh = "p", Auth = "a", UserAgent = "" });

        var list = await svc.ListAsync("u1");
        Assert.Single(list);
    }

    [Fact]
    public async Task Unsubscribe_returns_true_when_present_false_otherwise()
    {
        var svc = new MockPushSubscriptionService();
        var sub = await svc.SubscribeAsync("u1", new WebPushSubscriptionRequestDto
        {
            DeviceId = "d1", Endpoint = "x", P256dh = "p", Auth = "a", UserAgent = ""
        });

        Assert.True(await svc.UnsubscribeAsync("u1", sub.DeviceId));
        Assert.False(await svc.UnsubscribeAsync("u1", sub.DeviceId));
        Assert.Equal(0, await svc.CountAsync("u1"));
    }
}
