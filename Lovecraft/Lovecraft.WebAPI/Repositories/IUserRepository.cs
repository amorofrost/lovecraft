using System;
using System.Threading.Tasks;
using Lovecraft.Common.DataContracts;

namespace Lovecraft.WebAPI.Repositories
{
    public interface IUserRepository
    {
        Task<User> CreateAsync(User user);
        Task<User?> GetByIdAsync(Guid id);
        Task<User?> GetByTelegramUserIdAsync(long telegramUserId);
        Task<User?> GetByTelegramUsernameAsync(string telegramUsername);
        // Authenticate a user by login username and password. Returns the user on success, or null on failure.
        Task<User?> AuthenticateAsync(string username, string password);
    Task<User?> GetByUsernameAsync(string username);
        Task<User?> GetRandomAsync();
    }
}
