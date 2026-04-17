namespace Lovecraft.Backend.Services;

public class MockAppConfigService : IAppConfigService
{
    private static readonly AppConfig _config = new(
        RankThresholds.Defaults,
        PermissionConfig.Defaults);

    public Task<AppConfig> GetConfigAsync() => Task.FromResult(_config);
}
