using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Lovecraft.Backend.MockData;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Enums;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lovecraft.UnitTests;

// Note: DELETE /forum/replies/{id}, DELETE /forum/topics/{id}, and a dedicated pin endpoint
// are not implemented in the backend as of the Roles & ACL rollout. The permission keys
// delete_own_reply, delete_any_reply, delete_any_topic, and pin_topic are defined in
// appconfig so a later spec can wire them without requiring a reconfig. Pin is currently
// exercised indirectly via PUT /forum/topics/{id} with IsPinned (moderator-only path,
// covered by UpdateTopic_AsModerator_Succeeds).

[Collection("ForumTests")]
public class AclTests : IClassFixture<AclTests.TestAppFactory>, IDisposable
{
    private static readonly string[] TestUserIds =
    {
        "novice-user-1", "active-user-1",
        "novice-user-2", "novice-user-3", "novice-user-4", "active-user-2",
        "novice-reply", "active-reply",
        "author-user", "rando", "mod-user",
    };

    private static readonly string[] TestSectionIds = { "gated" };
    private static readonly string[] TestTopicIds = { "hidden-1", "hidden-2", "hidden-3", "norep-1", "norep-2", "own-1", "own-2", "own-3" };

    private readonly TestAppFactory _factory;

    public AclTests(TestAppFactory factory)
    {
        _factory = factory;
        ResetTestUsers();
        ResetTestForumData();
        SeedTestUsers();
    }

    public void Dispose()
    {
        ResetTestUsers();
        ResetTestForumData();
    }

    private static void ResetTestForumData()
    {
        foreach (var id in TestSectionIds)
            MockDataStore.ForumSections.RemoveAll(s => s.Id == id);
        foreach (var id in TestTopicIds)
            MockDataStore.ForumTopics.RemoveAll(t => t.Id == id);
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

    [Fact]
    public async Task GetTopicsInGatedSection_AsNovice_Returns403()
    {
        MockDataStore.ForumSections.Add(new ForumSectionDto
        {
            Id = "gated", Name = "Gated", Description = "", TopicCount = 0, MinRank = "activeMember"
        });
        using var client = _factory.CreateClientAsUser("novice-user-2");
        var resp = await client.GetAsync("/api/v1/forum/sections/gated/topics");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task NoviceHiddenTopic_NotInList_ForNovice()
    {
        MockDataStore.ForumTopics.Add(new ForumTopicDto
        {
            Id = "hidden-1", SectionId = "general", Title = "Hidden", Content = "...",
            AuthorId = "x", AuthorName = "x", MinRank = "novice", NoviceVisible = false,
        });
        using var client = _factory.CreateClientAsUser("novice-user-3");
        var resp = await client.GetAsync("/api/v1/forum/sections/general/topics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var payload = await resp.Content.ReadFromJsonAsync<ApiResponse<List<ForumTopicDto>>>();
        Assert.DoesNotContain(payload!.Data!, t => t.Id == "hidden-1");
    }

    [Fact]
    public async Task NoviceHiddenTopic_Visible_ForActiveMember()
    {
        MockDataStore.UserActivity["active-user-2"] = new MockUserActivity { ReplyCount = 5 };
        MockDataStore.ForumTopics.Add(new ForumTopicDto
        {
            Id = "hidden-2", SectionId = "general", Title = "Hidden 2", Content = "...",
            AuthorId = "x", AuthorName = "x", MinRank = "novice", NoviceVisible = false,
        });
        using var client = _factory.CreateClientAsUser("active-user-2");
        var resp = await client.GetAsync("/api/v1/forum/sections/general/topics");
        var payload = await resp.Content.ReadFromJsonAsync<ApiResponse<List<ForumTopicDto>>>();
        Assert.Contains(payload!.Data!, t => t.Id == "hidden-2");
    }

    [Fact]
    public async Task PostReply_WhenNoviceCantReply_AsNovice_Returns403()
    {
        MockDataStore.ForumTopics.Add(new ForumTopicDto
        {
            Id = "norep-1",
            SectionId = "general",
            Title = "NR",
            Content = "...",
            AuthorId = "x",
            AuthorName = "x",
            MinRank = "novice",
            NoviceVisible = true,
            NoviceCanReply = false,
        });
        using var client = _factory.CreateClientAsUser("novice-reply");
        var resp = await client.PostAsJsonAsync("/api/v1/forum/topics/norep-1/replies",
            new CreateReplyRequestDto { Content = "some text" });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task PostReply_WhenNoviceCantReply_AsActive_Succeeds()
    {
        MockDataStore.UserActivity["active-reply"] = new MockUserActivity { ReplyCount = 5 };
        MockDataStore.ForumTopics.Add(new ForumTopicDto
        {
            Id = "norep-2",
            SectionId = "general",
            Title = "NR2",
            Content = "...",
            AuthorId = "x",
            AuthorName = "x",
            MinRank = "novice",
            NoviceVisible = true,
            NoviceCanReply = false,
        });
        using var client = _factory.CreateClientAsUser("active-reply");
        var resp = await client.PostAsJsonAsync("/api/v1/forum/topics/norep-2/replies",
            new CreateReplyRequestDto { Content = "some text" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetHiddenTopic_ById_AsNovice_Returns403()
    {
        MockDataStore.ForumTopics.Add(new ForumTopicDto
        {
            Id = "hidden-3", SectionId = "general", Title = "H3", Content = "...",
            AuthorId = "x", AuthorName = "x", MinRank = "novice", NoviceVisible = false,
        });
        using var client = _factory.CreateClientAsUser("novice-user-4");
        var resp = await client.GetAsync("/api/v1/forum/topics/hidden-3");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateTopic_AsAuthor_Succeeds()
    {
        MockDataStore.ForumTopics.Add(new ForumTopicDto
        {
            Id = "own-1", SectionId = "general", Title = "Own", Content = "...",
            AuthorId = "author-user", AuthorName = "Author",
            NoviceVisible = true, NoviceCanReply = true,
        });
        using var client = _factory.CreateClientAsUser("author-user");
        var resp = await client.PutAsJsonAsync("/api/v1/forum/topics/own-1",
            new UpdateTopicRequestDto { NoviceVisible = false });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(MockDataStore.ForumTopics.First(t => t.Id == "own-1").NoviceVisible);
    }

    [Fact]
    public async Task UpdateTopic_AsRandomUser_Returns403()
    {
        MockDataStore.ForumTopics.Add(new ForumTopicDto
        {
            Id = "own-2", SectionId = "general", Title = "Own2", Content = "...",
            AuthorId = "someone-else", AuthorName = "X",
        });
        using var client = _factory.CreateClientAsUser("rando");
        var resp = await client.PutAsJsonAsync("/api/v1/forum/topics/own-2",
            new UpdateTopicRequestDto { NoviceVisible = false });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateTopic_AsModerator_Succeeds()
    {
        MockDataStore.ForumTopics.Add(new ForumTopicDto
        {
            Id = "own-3", SectionId = "general", Title = "Own3", Content = "...",
            AuthorId = "someone-else", AuthorName = "X",
        });
        using var client = _factory.CreateClientAsUser("mod-user", "moderator");
        var resp = await client.PutAsJsonAsync("/api/v1/forum/topics/own-3",
            new UpdateTopicRequestDto { IsPinned = true });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(MockDataStore.ForumTopics.First(t => t.Id == "own-3").IsPinned);
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
