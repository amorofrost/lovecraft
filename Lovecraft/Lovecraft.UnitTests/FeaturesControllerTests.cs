using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lovecraft.Common.DTOs.Features;
using Lovecraft.Common.Models;
using Xunit;

namespace Lovecraft.UnitTests;

/// <summary>
/// Integration tests for /api/v1/features. Public endpoint — no auth required.
/// </summary>
[Collection("FeaturesControllerTests")]
public class FeaturesControllerTests : IClassFixture<AclTests.TestAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNameCaseInsensitive = true,
    };

    private readonly AclTests.TestAppFactory _factory;

    public FeaturesControllerTests(AclTests.TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GET_features_returns_default_flags_without_auth()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/features");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<FeatureFlagsDto>>(JsonOpts);
        Assert.True(body!.Success);
        Assert.NotNull(body.Data);
        Assert.True(body.Data!.FeedEnabled);
    }
}
