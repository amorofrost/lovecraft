using Lovecraft.Backend.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Lovecraft.UnitTests;

public class EventInviteHasherTests
{
    [Fact]
    public void Hash_IsDeterministic()
    {
        var a = EventInviteHasher.Hash("abc", "pepper123456789012345678901234");
        var b = EventInviteHasher.Hash("abc", "pepper123456789012345678901234");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Hash_TrimsPlainCode()
    {
        var p = "pepper123456789012345678901234";
        Assert.Equal(EventInviteHasher.Hash("x", p), EventInviteHasher.Hash("  x  ", p));
    }
}

public class MockEventInviteServiceTests
{
    private static MockEventInviteService Svc()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = "unit-test-secret-key-min-32-characters!!" })
            .Build();
        return new MockEventInviteService(cfg);
    }

    [Fact]
    public async Task Validate_RoundTrip()
    {
        var svc = Svc();
        var (plain, exp) = await svc.CreateOrRotateInviteAsync("evt-1", DateTime.UtcNow.AddDays(7));
        var r = await svc.ValidatePlainCodeAsync(plain);
        Assert.NotNull(r);
        Assert.Equal("evt-1", r!.EventId);
    }

    [Fact]
    public async Task Validate_WrongCode_ReturnsNull()
    {
        var svc = Svc();
        await svc.CreateOrRotateInviteAsync("evt-1", DateTime.UtcNow.AddDays(7));
        Assert.Null(await svc.ValidatePlainCodeAsync("wrong"));
    }
}
