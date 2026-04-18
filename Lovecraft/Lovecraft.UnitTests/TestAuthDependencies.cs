using Lovecraft.Backend.Services;

namespace Lovecraft.UnitTests;

internal static class TestAuthDependencies
{
    /// <summary>Shared mock stack for constructing <see cref="MockAuthService"/> in unit tests.</summary>
    public static (MockAppConfigService App, MockEventInviteService Invites, MockEventService Events) CreateMockStack()
    {
        var app = new MockAppConfigService();
        var invites = new MockEventInviteService();
        var events = new MockEventService(new MockUserService(app));
        return (app, invites, events);
    }
}
