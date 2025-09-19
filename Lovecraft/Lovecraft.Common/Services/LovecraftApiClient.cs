using System.Net.Http.Json;
using Lovecraft.Common.DataContracts;
using Lovecraft.Common.Interfaces;

namespace Lovecraft.Common.Services;

public class LovecraftApiClient : ILovecraftApiClient
{
    private readonly HttpClient _http;

    public LovecraftApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<Lovecraft.Common.DataContracts.HealthInfo> GetHealthAsync()
    {
        var res = await _http.GetAsync("/health");
        res.EnsureSuccessStatusCode();
        var dto = await res.Content.ReadFromJsonAsync<Lovecraft.Common.DataContracts.HealthInfo>();
        if (dto != null) return dto;
        return new Lovecraft.Common.DataContracts.HealthInfo { Ready = true, Version = string.Empty, Uptime = TimeSpan.Zero };
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