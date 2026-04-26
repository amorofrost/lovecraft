using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Helpers;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Services.Azure;

public class AzureUserService : IUserService
{
    private readonly TableClient _usersTable;
    private readonly ILogger<AzureUserService> _logger;
    private readonly IAppConfigService _appConfig;

    public AzureUserService(
        TableServiceClient tableServiceClient,
        ILogger<AzureUserService> logger,
        IAppConfigService appConfig)
    {
        _logger = logger;
        _appConfig = appConfig;
        _usersTable = tableServiceClient.GetTableClient(TableNames.Users);
        _usersTable.CreateIfNotExistsAsync().GetAwaiter().GetResult();
    }

    public async Task<List<UserDto>> GetUsersAsync(int skip = 0, int take = 10)
    {
        var config = await _appConfig.GetConfigAsync();
        var results = new List<UserDto>();
        await foreach (var entity in _usersTable.QueryAsync<UserEntity>())
        {
            results.Add(ToDto(entity, config.Ranks));
        }
        return results.Skip(skip).Take(take).ToList();
    }

    public async Task<UserDto?> GetUserByIdAsync(string userId)
    {
        try
        {
            var config = await _appConfig.GetConfigAsync();
            var response = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(userId), userId);
            return ToDto(response.Value, config.Ranks);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<UserDto> UpdateUserAsync(string userId, UserDto dto)
    {
        try
        {
            var config = await _appConfig.GetConfigAsync();
            var response = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(userId), userId);
            var entity = response.Value;

            entity.Name = dto.Name;
            entity.Age = dto.Age;
            entity.Bio = dto.Bio;
            entity.Location = dto.Location;
            entity.Gender = dto.Gender.ToString();
            entity.ProfileImage = dto.ProfileImage;
            entity.ImagesJson = JsonSerializer.Serialize(dto.Images ?? new List<string>());
            entity.IsOnline = dto.IsOnline;
            entity.PreferencesJson = JsonSerializer.Serialize(dto.Preferences);
            entity.SettingsJson = JsonSerializer.Serialize(dto.Settings);
            entity.FavoriteSongJson = dto.FavoriteSong != null
                ? JsonSerializer.Serialize(dto.FavoriteSong)
                : string.Empty;
            entity.UpdatedAt = DateTime.UtcNow;

            await _usersTable.UpdateEntityAsync(entity, entity.ETag);
            return ToDto(entity, config.Ranks);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return dto;
        }
    }

    public async Task IncrementCounterAsync(string userId, UserCounter counter, int delta = 1)
    {
        // Read-modify-write with ETag. Concurrent increments to the same user
        // (e.g. rapid reply posts, or LikesReceived+MatchCount races on mutual
        // match) produce 412 Precondition Failed on the losing writer; retry
        // a bounded number of times with small jitter to spread the herd.
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await _usersTable.GetEntityAsync<UserEntity>(
                    UserEntity.GetPartitionKey(userId), userId);
                var entity = response.Value;
                switch (counter)
                {
                    case UserCounter.ReplyCount:     entity.ReplyCount     += delta; break;
                    case UserCounter.LikesReceived:  entity.LikesReceived  += delta; break;
                    case UserCounter.EventsAttended: entity.EventsAttended += delta; break;
                    case UserCounter.MatchCount:     entity.MatchCount     += delta; break;
                }
                entity.UpdatedAt = DateTime.UtcNow;
                await _usersTable.UpdateEntityAsync(entity, entity.ETag);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning("IncrementCounterAsync: user {UserId} not found", userId);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt < maxAttempts)
            {
                _logger.LogDebug(
                    "ETag conflict on counter {Counter} for {UserId}, attempt {Attempt}, retrying",
                    counter, userId, attempt);
                await Task.Delay(Random.Shared.Next(5, 25));
            }
        }
        // Unreachable: each attempt either returns on success/404, continues on
        // retryable 412, or lets the 412 bubble on the final attempt (filter is
        // false so the catch doesn't match). Caller shields with try/catch.
    }

    public async Task SetStaffRoleAsync(string userId, StaffRole role)
    {
        try
        {
            var response = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(userId), userId);
            var entity = response.Value;
            entity.StaffRole = role.ToString().ToLowerInvariant();
            entity.UpdatedAt = DateTime.UtcNow;
            await _usersTable.UpdateEntityAsync(entity, entity.ETag);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("SetStaffRoleAsync: user {UserId} not found", userId);
        }
    }

    public async Task SetRankOverrideAsync(string userId, UserRank? rank)
    {
        try
        {
            var response = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(userId), userId);
            var entity = response.Value;
            if (rank is null)
            {
                entity.RankOverride = null;
            }
            else
            {
                var raw = rank.Value.ToString();
                entity.RankOverride = string.IsNullOrEmpty(raw)
                    ? raw
                    : char.ToLowerInvariant(raw[0]) + raw[1..];
            }
            entity.UpdatedAt = DateTime.UtcNow;
            await _usersTable.UpdateEntityAsync(entity, entity.ETag);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("SetRankOverrideAsync: user {UserId} not found", userId);
        }
    }

    private static UserDto ToDto(UserEntity entity, RankThresholds ranks)
    {
        Enum.TryParse<Gender>(entity.Gender, ignoreCase: true, out var gender);

        UserPreferencesDto prefs;
        try { prefs = JsonSerializer.Deserialize<UserPreferencesDto>(entity.PreferencesJson) ?? new UserPreferencesDto(); }
        catch { prefs = new UserPreferencesDto(); }

        UserSettingsDto settings;
        try { settings = JsonSerializer.Deserialize<UserSettingsDto>(entity.SettingsJson) ?? new UserSettingsDto(); }
        catch { settings = new UserSettingsDto(); }

        List<string> images;
        try { images = JsonSerializer.Deserialize<List<string>>(entity.ImagesJson) ?? new List<string>(); }
        catch { images = new List<string>(); }

        AloeVeraSongDto? song = null;
        if (!string.IsNullOrEmpty(entity.FavoriteSongJson))
        {
            try { song = JsonSerializer.Deserialize<AloeVeraSongDto>(entity.FavoriteSongJson); }
            catch { }
        }

        if (!Enum.TryParse<StaffRole>(entity.StaffRole, ignoreCase: true, out var staffRole))
            staffRole = StaffRole.None;

        return new UserDto
        {
            Id = entity.RowKey,
            Name = entity.Name,
            Age = entity.Age,
            Bio = entity.Bio,
            Location = entity.Location,
            Gender = gender,
            ProfileImage = entity.ProfileImage,
            Images = images,
            LastSeen = entity.LastSeen,
            IsOnline = entity.IsOnline,
            Preferences = prefs,
            Settings = settings,
            FavoriteSong = song,
            Rank = RankCalculator.Compute(entity, ranks),
            StaffRole = staffRole,
            RegistrationSourceEventId = entity.RegistrationSourceEventId,
        };
    }
}
