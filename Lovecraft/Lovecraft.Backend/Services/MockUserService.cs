using Lovecraft.Common.DTOs.Users;
using Lovecraft.Backend.MockData;

namespace Lovecraft.Backend.Services;

public class MockUserService : IUserService
{
    public Task<List<UserDto>> GetUsersAsync(int skip = 0, int take = 10)
    {
        var users = MockDataStore.Users
            .Skip(skip)
            .Take(take)
            .ToList();
        return Task.FromResult(users);
    }

    public Task<UserDto?> GetUserByIdAsync(string userId)
    {
        var user = MockDataStore.Users.FirstOrDefault(u => u.Id == userId);
        return Task.FromResult(user);
    }

    public Task<UserDto> UpdateUserAsync(string userId, UserDto user)
    {
        var existingUser = MockDataStore.Users.FirstOrDefault(u => u.Id == userId);
        if (existingUser != null)
        {
            existingUser.Name = user.Name;
            existingUser.Age = user.Age;
            existingUser.Bio = user.Bio;
            existingUser.Location = user.Location;
        }
        return Task.FromResult(existingUser ?? user);
    }
}
