namespace Lovecraft.Backend.Services;

public interface IAppConfigService
{
    Task<AppConfig> GetConfigAsync();
}
