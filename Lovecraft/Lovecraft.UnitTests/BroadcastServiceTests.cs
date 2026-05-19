using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Notifications;
using Xunit;

public class BroadcastServiceTests
{
    [Fact]
    public async Task CreateAsync_ReturnsBroadcastWithGeneratedId()
    {
        var svc = new MockBroadcastService();
        var req = new CreateBroadcastRequestDto
        {
            Title = "Test",
            Body = "Body text",
            Link = "/aloevera",
            Audience = new BroadcastAudienceDto("all", null)
        };
        var bc = await svc.CreateAsync(req, issuedByUserId: "admin-1");
        Assert.False(string.IsNullOrEmpty(bc.Id));
        Assert.Equal("admin-1", bc.IssuedByUserId);
        Assert.Equal("pending", bc.Status);
        Assert.Equal(0, bc.DispatchedCount);
    }

    [Fact]
    public async Task ListAsync_ReturnsNewestFirst()
    {
        var svc = new MockBroadcastService();
        var a = await svc.CreateAsync(new CreateBroadcastRequestDto
        {
            Title = "A", Body = "a", Audience = new BroadcastAudienceDto("all", null)
        }, "admin-1");
        await Task.Delay(20);
        var b = await svc.CreateAsync(new CreateBroadcastRequestDto
        {
            Title = "B", Body = "b", Audience = new BroadcastAudienceDto("all", null)
        }, "admin-1");

        var list = await svc.ListAsync(limit: 10);
        Assert.Equal(b.Id, list[0].Id);
        Assert.Equal(a.Id, list[1].Id);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var svc = new MockBroadcastService();
        var result = await svc.GetByIdAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetCompletedAsync_UpdatesStatusAndCount()
    {
        var svc = new MockBroadcastService();
        var bc = await svc.CreateAsync(new CreateBroadcastRequestDto
        {
            Title = "Test", Body = "Body", Audience = new BroadcastAudienceDto("all", null)
        }, "admin-1");

        await svc.SetCompletedAsync(bc.Id, dispatchedCount: 42, completedAtUtc: DateTime.UtcNow);

        var updated = await svc.GetByIdAsync(bc.Id);
        Assert.NotNull(updated);
        Assert.Equal("completed", updated!.Status);
        Assert.Equal(42, updated.DispatchedCount);
        Assert.NotNull(updated.CompletedAtUtc);
    }
}
