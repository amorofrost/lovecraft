using System.Net.Http;
using System.Threading.Tasks;

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public virtual async Task<string> GetWeatherAsync()
    {
        var res = await _http.GetAsync("/WeatherForecast");
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync();
    }
}
