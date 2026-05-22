namespace Lovecraft.Backend.Services;

public interface IAppConfigService
{
    Task<AppConfig> GetConfigAsync();
    Task<MetricsConfig> GetMetricsConfigAsync(CancellationToken ct = default);
    /// <summary>Persists all seven metrics config rows to the backing store.</summary>
    Task SetMetricsConfigAsync(MetricsConfig config, CancellationToken ct = default);
    Task InvalidateAsync();
}
