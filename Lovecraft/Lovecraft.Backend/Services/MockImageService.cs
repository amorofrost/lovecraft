using Lovecraft.Backend.MockData;

namespace Lovecraft.Backend.Services;

public class MockImageService : IImageService
{
    public Task<string> UploadProfileImageAsync(string userId, Stream imageStream, string contentType)
    {
        var user = MockDataStore.Users.FirstOrDefault(u => u.Id == userId);
        return Task.FromResult(user?.ProfileImage ?? string.Empty);
    }
}
