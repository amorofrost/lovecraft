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
    }
}
