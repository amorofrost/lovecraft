using Lovecraft.Backend.MockData;

namespace Lovecraft.Backend.Services;

public class MockImageService : IImageService
{
    public Task<string> UploadProfileImageAsync(string userId, Stream imageStream, string contentType)
    {
        var user = MockDataStore.Users.FirstOrDefault(u => u.Id == userId);
        return Task.FromResult(user?.ProfileImage ?? string.Empty);
    }

    public Task<string> UploadContentImageAsync(string userId, Stream imageStream, string contentType)
    {
        return Task.FromResult("https://placehold.co/600x400");
    }
}
