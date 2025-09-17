using System.Threading.Tasks;

namespace Lovecraft.Common
{
    public interface ILovecraftApiClient
    {
        Task<string> GetWeatherAsync();
    }
}
