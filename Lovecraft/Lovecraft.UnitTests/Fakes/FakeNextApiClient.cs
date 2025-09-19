namespace Lovecraft.UnitTests.Fakes;

using Lovecraft.Common.Interfaces;

internal class FakeNextApiClient : ILovecraftApiClient
{
    private readonly Lovecraft.Common.DataContracts.User _next;

    public FakeNextApiClient(Lovecraft.Common.DataContracts.User next)
    {
        _next = next;
    }

    public Task<Lovecraft.Common.DataContracts.HealthInfo> GetHealthAsync()
    {
        return Task.FromResult(new Lovecraft.Common.DataContracts.HealthInfo { Ready = true, Version = "test", Uptime = System.TimeSpan.FromSeconds(1) });
    }

    public Task<Lovecraft.Common.DataContracts.User> CreateUserAsync(Lovecraft.Common.DataContracts.CreateUserRequest req)
    {
        throw new System.NotImplementedException();
    }

    public Task<Lovecraft.Common.DataContracts.User?> GetUserByIdAsync(System.Guid id)
    {
        return Task.FromResult<Lovecraft.Common.DataContracts.User?>(null);
    }

    public Task<Lovecraft.Common.DataContracts.User?> GetUserByTelegramUserIdAsync(long telegramUserId)
    {
        return Task.FromResult<Lovecraft.Common.DataContracts.User?>(null);
    }

    public Task<Lovecraft.Common.DataContracts.User?> GetUserByTelegramUsernameAsync(string username)
    {
        return Task.FromResult<Lovecraft.Common.DataContracts.User?>(null);
    }

    public Task<Lovecraft.Common.DataContracts.User?> GetNextProfileAsync()
    {
        return Task.FromResult<Lovecraft.Common.DataContracts.User?>(_next);
    }
}
