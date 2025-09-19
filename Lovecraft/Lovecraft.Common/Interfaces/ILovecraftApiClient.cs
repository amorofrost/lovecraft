using Lovecraft.Common.DataContracts;

namespace Lovecraft.Common.Interfaces;

public interface ILovecraftApiClient
{
    Task<Lovecraft.Common.DataContracts.HealthInfo> GetHealthAsync();

    Task<User> CreateUserAsync(CreateUserRequest req);
    Task<User?> GetUserByIdAsync(Guid id);
    Task<User?> GetUserByTelegramUserIdAsync(long telegramUserId);
    Task<User?> GetUserByTelegramUsernameAsync(string username);
}

