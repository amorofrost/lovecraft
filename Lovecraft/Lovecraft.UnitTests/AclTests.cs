using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Lovecraft.Backend.MockData;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lovecraft.UnitTests;

[Collection("ForumTests")]
public class AclTests : IClassFixture<AclTests.TestAppFactory>, IDisposable
{
    private static readonly string[] TestUserIds = { "novice-user-1", "active-user-1" };

    private readonly TestAppFactory _factory;

    public AclTests(TestAppFactory factory)
    {
        _factory = factory;
        ResetTestUsers();
        SeedTestUsers();
    }

    public void Dispose()
    {
        ResetTestUsers();
    }

    /// <summary>
    /// Remove only the specific test-user keys rather than calling Clear() on the
    /// global MockDataStore dictionaries — Clear() would race with parallel test
    /// collections (e.g. ServiceTests) that manipulate UserActivity for user "1".
    /// </summary>
    private static void ResetTestUsers()
    {
        foreach (var id in TestUserIds)
        {
            MockDataStore.UserActivity.Remove(id);
            MockDataStore.UserStaffRoles.Remove(id);
            MockDataStore.UserRankOverrides.Remove(id);
            MockDataStore.Users.RemoveAll(u => u.Id == id);
        }
    }

    /// <summary>
    /// MockUserService.GetUserByIdAsync only returns users present in MockDataStore.Users,
    /// so we seed minimal DTOs for the virtual ACL test users. Rank is still computed
    /// from UserActivity during AugmentWithRank.
    /// </summary>
    private static void SeedTestUsers()
    {
        foreach (var id in TestUserIds)
        {
            MockDataStore.Users.Add(new UserDto { Id = id, Name = id });
        }
    }

    [Fact]
    public async Task CreateTopic_AsNovice_ReturnsInsufficientRank()
    {
        using var client = _factory.CreateClientAsUser("novice-user-1");
        var body = new CreateTopicRequestDto
        {
            Title = "Title that is long enough",
            Content = "Content that is long enough too.",
        };
        var resp = await client.PostAsJsonAsync("/api/v1/forum/sections/general/topics", body);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var payload = await resp.Content.ReadFromJsonAsync<ApiResponse<ForumTopicDto>>();
        Assert.Equal("INSUFFICIENT_RANK", payload!.Error!.Code);
    }

    [Fact]
    public async Task CreateTopic_AsActiveMember_Succeeds()
    {
        MockDataStore.UserActivity["active-user-1"] = new MockUserActivity { ReplyCount = 5 };

        using var client = _factory.CreateClientAsUser("active-user-1");
        var body = new CreateTopicRequestDto
        {
            Title = "A valid topic title",
            Content = "Content that's definitely long enough.",
        };
        var resp = await client.PostAsJsonAsync("/api/v1/forum/sections/general/topics", body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    public class TestAppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("USE_AZURE_STORAGE", "false");
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                // Replace JWT with a header-driven test scheme. Overriding defaults also
                // makes [Authorize] (which has no explicit scheme) pick up the Test scheme.
                services.Configure<AuthenticationOptions>(o =>
                {
                    o.DefaultAuthenticateScheme = "Test";
                    o.DefaultChallengeScheme = "Test";
                    o.DefaultScheme = "Test";
                });
                services.AddAuthentication("Test")
                    .AddScheme<TestAuthOptions, TestAuthHandler>("Test", _ => { });
            });
        }

        public HttpClient CreateClientAsUser(string userId, string staffRole = "none")
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-User", userId);
            client.DefaultRequestHeaders.Add("X-Test-StaffRole", staffRole);
            return client;
        }
    }
}

public class TestAuthOptions : AuthenticationSchemeOptions { }

public class TestAuthHandler : AuthenticationHandler<TestAuthOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<TestAuthOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers["X-Test-User"].ToString();
        if (string.IsNullOrEmpty(userId))
            return Task.FromResult(AuthenticateResult.NoResult());
        var staffRole = Request.Headers["X-Test-StaffRole"].ToString();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId),
            new("staffRole", string.IsNullOrEmpty(staffRole) ? "none" : staffRole),
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), "Test");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
