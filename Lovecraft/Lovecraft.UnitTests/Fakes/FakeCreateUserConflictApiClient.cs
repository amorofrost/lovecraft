namespace Lovecraft.UnitTests.Fakes;

using Lovecraft.Common.Interfaces;

internal class FakeCreateUserConflictApiClient : ILovecraftApiClient
{
    public Task<Lovecraft.Common.DataContracts.HealthInfo> GetHealthAsync()
    {
        return Task.FromResult(new Lovecraft.Common.DataContracts.HealthInfo { Ready = true, Version = "test", Uptime = System.TimeSpan.FromSeconds(1) });
    }

    public Task<Lovecraft.Common.DataContracts.User> CreateUserAsync(Lovecraft.Common.DataContracts.CreateUserRequest req)
    {
        // Simulate a server-side 409 Conflict by throwing an HttpRequestException with StatusCode set to Conflict.
        // Use the constructor overload that accepts a status code so the handler's reflection-based check will find it.
        throw new System.Net.Http.HttpRequestException("Conflict", null, System.Net.HttpStatusCode.Conflict);
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
