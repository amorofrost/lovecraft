using Lovecraft.Backend.Services.Metrics;
using Xunit;

namespace Lovecraft.UnitTests;

public class MetricsRouteNormalizerTests
{
    [Theory]
    [InlineData("/api/v1/users/55126c3e-21fd-457c-9953-dc66f83186b3", "api~v1~users~{id}")]
    [InlineData("/api/v1/users/42", "api~v1~users~{id}")]
    [InlineData("api/v1/users/{id:guid}", "api~v1~users~{id}")]
    [InlineData("api/v1/forum/sections/{sectionId}/topics", "api~v1~forum~sections~{sectionId}~topics")]
    [InlineData("/api/v1/users/me", "api~v1~users~me")]
    [InlineData("/api/v1/forum/event-discussions/summary", "api~v1~forum~event-discussions~summary")]
    [InlineData("/api/v1/users/42/images", "api~v1~users~{id}~images")]
    [InlineData("/api/v1/events?code=ABC", "api~v1~events")]
    public void Normalize_ProducesExpectedShape(string input, string expected)
    {
        Assert.Equal(expected, MetricsRouteNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("/api/v1/users/55126c3e-21fd-457c-9953-dc66f83186b3")]
    [InlineData("api/v1/forum/topics/{topicId:guid}")]
    [InlineData("api/v1/files/{*path}")]
    public void Normalize_OutputHasNoAzureForbiddenChars(string input)
    {
        var result = MetricsRouteNormalizer.Normalize(input);
        Assert.DoesNotContain('/', result);
        Assert.DoesNotContain('\\', result);
        Assert.DoesNotContain('#', result);
        Assert.DoesNotContain('?', result);
    }
}
