using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Notifications;
using Xunit;

namespace Lovecraft.UnitTests;

[Collection("ChatTests")] // serialize: MockDataStore.FcmSubscriptions is shared static
public class FcmSubscriptionServiceTests
{
    private static MockFcmSubscriptionService Svc()
    {
        MockDataStore.FcmSubscriptions.Clear();
        return new MockFcmSubscriptionService();
    }

    private static FcmRegisterRequestDto Req(string? deviceId = "dev-1", string token = "tok-1") =>
        new() { DeviceId = deviceId, Token = token, Platform = "android", DeviceModel = "Pixel" };

    [Fact]
    public async Task Register_StoresToken()
    {
        var svc = Svc();
        var dto = await svc.RegisterAsync("u1", Req());
        Assert.Equal("dev-1", dto.DeviceId);
        Assert.Equal("tok-1", dto.Token);
        Assert.Equal(1, await svc.CountAsync("u1"));
    }

    [Fact]
    public async Task Register_GeneratesDeviceId_WhenOmitted()
    {
        var svc = Svc();
        var dto = await svc.RegisterAsync("u1", Req(deviceId: null));
        Assert.False(string.IsNullOrEmpty(dto.DeviceId));
    }

    [Fact]
    public async Task Register_SameDevice_UpdatesTokenAndKeepsCreatedAt()
    {
        var svc = Svc();
        var first = await svc.RegisterAsync("u1", Req(token: "old"));
        var second = await svc.RegisterAsync("u1", Req(token: "new"));
        Assert.Equal("new", second.Token);
        Assert.Equal(first.CreatedAtUtc, second.CreatedAtUtc);
        Assert.Equal(1, await svc.CountAsync("u1"));
    }

    [Fact]
    public async Task List_ReturnsOnlyUsersDevices()
    {
        var svc = Svc();
        await svc.RegisterAsync("u1", Req("d1"));
        await svc.RegisterAsync("u2", Req("d2"));
        var list = await svc.ListAsync("u1");
        Assert.Single(list);
        Assert.Equal("d1", list[0].DeviceId);
    }

    [Fact]
    public async Task Unregister_RemovesDevice()
    {
        var svc = Svc();
        await svc.RegisterAsync("u1", Req("d1"));
        Assert.True(await svc.UnregisterAsync("u1", "d1"));
        Assert.Equal(0, await svc.CountAsync("u1"));
    }

    [Fact]
    public async Task Unregister_ReturnsFalse_WhenAbsent()
    {
        var svc = Svc();
        Assert.False(await svc.UnregisterAsync("u1", "ghost"));
    }
}
