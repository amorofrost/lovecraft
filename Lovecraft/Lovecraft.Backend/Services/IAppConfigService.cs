namespace Lovecraft.Backend.Services;

public interface IAppConfigService
{
    Task<AppConfig> GetConfigAsync();
    Task<MetricsConfig> GetMetricsConfigAsync(CancellationToken ct = default);
    Task InvalidateAsync();
}
