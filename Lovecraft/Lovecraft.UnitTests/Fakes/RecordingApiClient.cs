namespace Lovecraft.UnitTests.Fakes;

using Lovecraft.Common.Interfaces;

internal class RecordingApiClient : ILovecraftApiClient
{
    public Lovecraft.Common.DataContracts.CreateUserRequest? CreatedRequest;

    public Task<Lovecraft.Common.DataContracts.HealthInfo> GetHealthAsync()
    {
        return Task.FromResult(new Lovecraft.Common.DataContracts.HealthInfo { Ready = true, Version = "test", Uptime = System.TimeSpan.FromSeconds(1) });
    }

    public Task<Lovecraft.Common.DataContracts.User> CreateUserAsync(Lovecraft.Common.DataContracts.CreateUserRequest req)
    {
        CreatedRequest = req;
        var u = new Lovecraft.Common.DataContracts.User
        {
            Id = System.Guid.NewGuid(),
            Name = req.Name,
            AvatarUri = req.AvatarUri,
            TelegramUserId = req.TelegramUserId,
            TelegramUsername = req.TelegramUsername,
            TelegramAvatarFileId = req.TelegramAvatarFileId,
            CreatedAt = System.DateTime.UtcNow,
            Version = System.Guid.NewGuid().ToString()
        };
        return Task.FromResult(u);
    }

    public Task<Lovecraft.Common.DataContracts.User?> GetUserByIdAsync(System.Guid id)
    {
        return Task.FromResult<Lovecraft.Common.DataContracts.User?>(null);
    }

    public Task<Lovecraft.Common.DataContracts.User?> GetUserByTelegramUserIdAsync(long telegramUserId)
    {
        // For registration tests, return null to indicate user not found
        return Task.FromResult<Lovecraft.Common.DataContracts.User?>(null);
    }

    public Task<Lovecraft.Common.DataContracts.User?> GetUserByTelegramUsernameAsync(string username)
    {
        return Task.FromResult<Lovecraft.Common.DataContracts.User?>(null);
    }

    public Task<bool> IsUsernameAvailableAsync(string username)
    {
        return Task.FromResult(true);
    }

    public Task<Lovecraft.Common.DataContracts.User?> GetNextProfileAsync()
    {
        return Task.FromResult<Lovecraft.Common.DataContracts.User?>(null);
    }
}