using System.Net.Http;
using System.Threading.Tasks;

namespace Lovecraft.Common
{
    public class LovecraftApiClient : ILovecraftApiClient
    {
        private readonly HttpClient _http;

        public LovecraftApiClient(HttpClient http)
        {
            _http = http;
        }

        public async Task<string> GetWeatherAsync()
        {
            var res = await _http.GetAsync("/WeatherForecast");
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadAsStringAsync();
        }
    }
}
