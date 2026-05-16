using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lovecraft.Backend.MockData;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lovecraft.UnitTests;

/// <summary>
/// Integration tests for PUT /api/v1/users/{id} — validates prompts and image cap
/// constraints added in the profile-depth feature.
/// </summary>
[Collection("UsersControllerUpdateTests")]
public class UsersControllerUpdateTests : IClassFixture<AclTests.TestAppFactory>, IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNameCaseInsensitive = true,
    };

    private readonly AclTests.TestAppFactory _factory;
    private readonly HttpClient _client;
    private const string UserId = "test-user-001";

    public UsersControllerUpdateTests(AclTests.TestAppFactory factory)
    {
        _factory = factory;

        // Ensure the user exists in MockDataStore so UpdateUserAsync can find it
        // and return an augmented DTO (not just the raw input).
        if (!MockDataStore.Users.Any(u => u.Id == UserId))
        {
            MockDataStore.Users.Add(new UserDto
            {
                Id = UserId,
                Name = "Test User",
                Age = 25,
                Bio = "Hi",
                Location = "Moscow",
            });
        }

        _client = _factory.CreateClientAsUser(UserId);
    }

    public void Dispose()
    {
        MockDataStore.Users.RemoveAll(u => u.Id == UserId);
    }

    private static UserDto BaseValidDto() => new()
    {
        Id = UserId,
        Name = "Test",
        Age = 25,
        Bio = "Hi",
        Location = "Moscow",
    };

    [Fact]
    public async Task UpdateUser_RejectsMoreThanThreePrompts()
    {
        var dto = BaseValidDto();
        // Use distinct valid prompt IDs to avoid DUPLICATE_PROMPT_ID being triggered first
        dto.Prompts = new List<PromptAnswerDto>
        {
            new() { PromptId = "aloevera_first",   Answer = "a0" },
            new() { PromptId = "aloevera_song",    Answer = "a1" },
            new() { PromptId = "concert_memory",   Answer = "a2" },
            new() { PromptId = "looking_for",      Answer = "a3" },
        };
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("PROMPTS_TOO_MANY", body!.Error?.Code);
    }

    [Fact]
    public async Task UpdateUser_RejectsUnknownPromptId()
    {
        var dto = BaseValidDto();
        dto.Prompts = new List<PromptAnswerDto>
        {
            new() { PromptId = "totally_invented", Answer = "hello" },
        };
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("UNKNOWN_PROMPT_ID", body!.Error?.Code);
    }

    [Fact]
    public async Task UpdateUser_RejectsDuplicatePromptId()
    {
        var dto = BaseValidDto();
        dto.Prompts = new List<PromptAnswerDto>
        {
            new() { PromptId = "looking_for", Answer = "a" },
            new() { PromptId = "looking_for", Answer = "b" },
        };
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("DUPLICATE_PROMPT_ID", body!.Error?.Code);
    }

    [Fact]
    public async Task UpdateUser_RejectsAnswerOver200Chars()
    {
        var dto = BaseValidDto();
        dto.Prompts = new List<PromptAnswerDto>
        {
            new() { PromptId = "looking_for", Answer = new string('a', 201) },
        };
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("PROMPT_ANSWER_TOO_LONG", body!.Error?.Code);
    }

    [Fact]
    public async Task UpdateUser_RejectsHtmlInAnswer()
    {
        var dto = BaseValidDto();
        dto.Prompts = new List<PromptAnswerDto>
        {
            new() { PromptId = "looking_for", Answer = "<b>hi</b>" },
        };
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("HTML_NOT_ALLOWED", body!.Error?.Code);
    }

    [Fact]
    public async Task UpdateUser_RejectsMoreThanSixImages()
    {
        var dto = BaseValidDto();
        dto.Images = Enumerable.Range(0, 7).Select(i => $"https://example.com/{i}.jpg").ToList();
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("IMAGES_TOO_MANY", body!.Error?.Code);
    }

    [Fact]
    public async Task UpdateUser_AcceptsValidPromptsAndImages()
    {
        var dto = BaseValidDto();
        dto.Prompts = new List<PromptAnswerDto>
        {
            new() { PromptId = "looking_for", Answer = "Tour buddies" },
        };
        dto.Images = new List<string> { "https://example.com/1.jpg" };
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>(JsonOpts);
        Assert.True(body!.Success);
        Assert.NotNull(body.Data!.Prompts);
        Assert.Single(body.Data.Prompts);
    }

    [Fact]
    public async Task UpdateUser_AcceptsNullPrompts()
    {
        var dto = BaseValidDto();
        dto.Prompts = null;
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Country / Region tests ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateUser_AcceptsCountryAndRegion()
    {
        var dto = BaseValidDto();
        dto.Country = "RU";
        dto.Region = "Москва";
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>(JsonOpts);
        Assert.True(body!.Success);
        Assert.Equal("RU", body.Data!.Country);
        Assert.Equal("Москва", body.Data.Region);
    }

    [Fact]
    public async Task UpdateUser_RejectsCountryWithHtml()
    {
        var dto = BaseValidDto();
        dto.Country = "<b>RU</b>";
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("HTML_NOT_ALLOWED", body!.Error?.Code);
    }

    [Fact]
    public async Task UpdateUser_RejectsRegionWithHtml()
    {
        var dto = BaseValidDto();
        dto.Region = "<i>Moscow</i>";
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("HTML_NOT_ALLOWED", body!.Error?.Code);
    }

    [Fact]
    public async Task UpdateUser_RejectsCountryTooLong()
    {
        var dto = BaseValidDto();
        dto.Country = new string('a', 57);
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("COUNTRY_TOO_LONG", body!.Error?.Code);
    }

    [Fact]
    public async Task UpdateUser_RejectsRegionTooLong()
    {
        var dto = BaseValidDto();
        dto.Region = new string('a', 81);
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("REGION_TOO_LONG", body!.Error?.Code);
    }

    // ── SecondaryCountry / SecondaryRegion tests ────────────────────────────

    [Fact]
    public async Task UpdateUser_AcceptsSecondaryCountryAndRegion()
    {
        var dto = BaseValidDto();
        dto.Country = "RU";
        dto.Region = "Москва";
        dto.SecondaryCountry = "TH";
        dto.SecondaryRegion = "Пхукет";
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>(JsonOpts);
        Assert.Equal("TH", body!.Data!.SecondaryCountry);
        Assert.Equal("Пхукет", body.Data.SecondaryRegion);
    }

    [Fact]
    public async Task UpdateUser_RejectsSecondaryCountryWithHtml()
    {
        var dto = BaseValidDto();
        dto.SecondaryCountry = "<b>TH</b>";
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("HTML_NOT_ALLOWED", body!.Error?.Code);
    }

    [Fact]
    public async Task UpdateUser_RejectsSecondaryRegionWithHtml()
    {
        var dto = BaseValidDto();
        dto.SecondaryRegion = "<i>Phuket</i>";
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("HTML_NOT_ALLOWED", body!.Error?.Code);
    }

    [Fact]
    public async Task UpdateUser_RejectsSecondaryCountryTooLong()
    {
        var dto = BaseValidDto();
        dto.SecondaryCountry = new string('a', 57);
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("SECONDARY_COUNTRY_TOO_LONG", body!.Error?.Code);
    }

    [Fact]
    public async Task UpdateUser_RejectsSecondaryRegionTooLong()
    {
        var dto = BaseValidDto();
        dto.SecondaryRegion = new string('a', 81);
        var resp = await _client.PutAsJsonAsync($"/api/v1/users/{UserId}", dto);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("SECONDARY_REGION_TOO_LONG", body!.Error?.Code);
    }
}
