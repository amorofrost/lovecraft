using Lovecraft.Backend.Services;
using Xunit;

namespace Lovecraft.UnitTests;

public class EventInviteNormalizerTests
{
    [Fact]
    public void Normalize_TrimsAndUppercases()
    {
        Assert.Equal("AB-CD", EventInviteNormalizer.Normalize("  ab-cd  "));
    }

    [Fact]
    public void Normalize_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, EventInviteNormalizer.Normalize("   "));
    }
}

public class EventInviteHelpersTests
{
    [Theory]
    [InlineData("-1", true)]
    [InlineData("-99", true)]
    [InlineData("1", false)]
    [InlineData("evt-1", false)]
    [InlineData("", false)]
    public void IsCampaignEventId_Classifies(string id, bool expected)
    {
        Assert.Equal(expected, EventInviteHelpers.IsCampaignEventId(id));
    }
}

public class MockEventInviteServiceTests
{
    private static MockEventInviteService Svc() => new();

    [Fact]
    public async Task Validate_RoundTrip_EventInvite()
    {
        var svc = Svc();
        var (plain, _) = await svc.CreateOrRotateInviteAsync("evt-1", DateTime.UtcNow.AddDays(7));
        var r = await svc.ValidatePlainCodeAsync(plain);
        Assert.NotNull(r);
        Assert.Equal("evt-1", r!.EventId);
        Assert.Equal(EventInviteNormalizer.Normalize(plain), r.NormalizedPlainCode);
    }

    [Fact]
    public async Task Validate_WrongCode_ReturnsNull()
    {
        var svc = Svc();
        await svc.CreateOrRotateInviteAsync("evt-1", DateTime.UtcNow.AddDays(7));
        Assert.Null(await svc.ValidatePlainCodeAsync("wrong"));
    }

    [Fact]
    public async Task CampaignInvite_DoesNotRegisterAsEventRow()
    {
        var svc = Svc();
        var (plain, _) = await svc.CreateCampaignInviteAsync("-1", "ads", DateTime.UtcNow.AddDays(30), plainCodeOverride: "CAMP-TEST-1");
        var r = await svc.ValidatePlainCodeAsync(plain);
        Assert.NotNull(r);
        Assert.Equal("-1", r!.EventId);
    }

    [Fact]
    public async Task CreateOrRotate_WithCustomPlain_UsesCode()
    {
        var svc = Svc();
        var (plain, _) = await svc.CreateOrRotateInviteAsync(
            "evt-custom",
            DateTime.UtcNow.AddDays(7),
            plainCodeOverride: "MY-EVENT-CODE-1");
        Assert.Equal("MY-EVENT-CODE-1", plain);
        var r = await svc.ValidatePlainCodeAsync("my-event-code-1");
        Assert.NotNull(r);
        Assert.Equal("evt-custom", r!.EventId);
    }

    [Fact]
    public async Task IncrementRegistration_Increments()
    {
        var svc = Svc();
        var (plain, _) = await svc.CreateOrRotateInviteAsync("evt-9", DateTime.UtcNow.AddDays(7));
        await svc.IncrementRegistrationCountAsync(plain);
        var list = await svc.ListInvitesAsync();
        var row = list.First(i => i.PlainCode == EventInviteNormalizer.Normalize(plain));
        Assert.Equal(1, row.RegistrationCount);
    }
}
