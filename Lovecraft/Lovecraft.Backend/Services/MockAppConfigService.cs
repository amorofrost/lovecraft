namespace Lovecraft.Backend.Services;

public class MockAppConfigService : IAppConfigService
{
    private AppConfig _config = new(
        RankThresholds.Defaults,
        PermissionConfig.Defaults,
        RegistrationConfig.Defaults,
        FeatureFlagsConfig.Defaults,
        MetricsConfig.Defaults);

    public Task<AppConfig> GetConfigAsync() => Task.FromResult(_config);

    public Task<MetricsConfig> GetMetricsConfigAsync(CancellationToken ct = default) => Task.FromResult(_config.Metrics);

    public Task InvalidateAsync() => Task.CompletedTask;

    // Test helper — allows overriding the metrics config returned by both getters.
    public void SetMetricsConfig(MetricsConfig cfg) => _config = _config with { Metrics = cfg };
}
