using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
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

    public async Task<bool> IsUsernameAvailableAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        var res = await _http.GetAsync($"/api/users/usernameAvailable/{Uri.EscapeDataString(username)}");
        if (res.StatusCode == HttpStatusCode.BadRequest) return false;
        if (res.StatusCode == HttpStatusCode.Conflict) return false;
        res.EnsureSuccessStatusCode();
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        if (dto.ValueKind == JsonValueKind.Object && dto.TryGetProperty("available", out var availableProp) && availableProp.ValueKind == JsonValueKind.True)
            return true;
        return false;
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

    public async Task<User?> GetNextProfileAsync()
    {
        var res = await _http.GetAsync($"/api/users/next");
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<User>();
    }

    public async Task<User?> AuthenticateAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;

        var payload = new { Username = username, Password = password };
        var res = await _http.PostAsJsonAsync("/api/users/authenticate", payload);
        if (res.StatusCode == HttpStatusCode.Unauthorized) return null;
        if (res.StatusCode == HttpStatusCode.BadRequest) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<User>();
    }
}