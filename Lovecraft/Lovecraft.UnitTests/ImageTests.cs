using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;

namespace Lovecraft.UnitTests;

[Collection("ImageTests")]
public class ImageTests
{
    [Fact]
    public async Task MockImageService_UploadProfileImageAsync_ReturnsNonNullUrl()
    {
        var service = new MockImageService();
        var userId = MockDataStore.Users[0].Id;

        var result = await service.UploadProfileImageAsync(userId, Stream.Null, "image/jpeg");

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Equal(MockDataStore.Users[0].ProfileImage, result);
    }
}
