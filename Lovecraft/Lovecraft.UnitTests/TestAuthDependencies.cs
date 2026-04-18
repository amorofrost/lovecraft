using Lovecraft.Backend.Services;
using Microsoft.Extensions.Configuration;

namespace Lovecraft.UnitTests;

internal static class TestAuthDependencies
{
    /// <summary>Shared mock stack for constructing <see cref="MockAuthService"/> in unit tests.</summary>
    public static (MockAppConfigService App, MockEventInviteService Invites, MockEventService Events) CreateMockStack()
    {
        var app = new MockAppConfigService();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-key-min-32-characters!!",
            })
            .Build();
        var invites = new MockEventInviteService(cfg);
        var events = new MockEventService(new MockUserService(app));
        return (app, invites, events);
    }
}
