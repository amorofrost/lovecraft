using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System;
using Lovecraft.Common.DataContracts;


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

        public async Task<User> CreateUserAsync(CreateUserRequest req)
        {
            var res = await _http.PostAsJsonAsync("/api/users", req);
            res.EnsureSuccessStatusCode();
            var user = await res.Content.ReadFromJsonAsync<User>();
            if (user == null) throw new InvalidOperationException("Failed to deserialize created user");
            return user;
        }

        public async Task<User?> GetUserByIdAsync(Guid id)
        {
            var res = await _http.GetAsync($"/api/users/{id}");
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<User>();
        }

        public async Task<User?> GetUserByTelegramUserIdAsync(long telegramUserId)
        {
            var res = await _http.GetAsync($"/api/users/byTelegramId/{telegramUserId}");
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<User>();
        }

        public async Task<User?> GetUserByTelegramUsernameAsync(string username)
        {
            var res = await _http.GetAsync($"/api/users/byTelegramUsername/{Uri.EscapeDataString(username)}");
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<User>();
        }
    }
}
