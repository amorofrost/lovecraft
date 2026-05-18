using Lovecraft.Backend.Helpers;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Services;

public class MockUserService : IUserService
{
    private readonly IAppConfigService _appConfig;

    public MockUserService(IAppConfigService appConfig)
    {
        _appConfig = appConfig;
    }

    public async Task<List<UserDto>> GetUsersAsync(int skip = 0, int take = 10, string? country = null, string? region = null)
    {
        var config = await _appConfig.GetConfigAsync();
        var query = MockDataStore.Users.AsEnumerable();

        var hasCountry = !string.IsNullOrWhiteSpace(country);
        var hasRegion  = !string.IsNullOrWhiteSpace(region);

        if (hasCountry && hasRegion)
            query = query.Where(u =>
                (string.Equals(u.Country, country, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(u.Region,  region,  StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(u.SecondaryCountry, country, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(u.SecondaryRegion,  region,  StringComparison.OrdinalIgnoreCase)));
        else if (hasCountry)
            query = query.Where(u =>
                string.Equals(u.Country, country, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.SecondaryCountry, country, StringComparison.OrdinalIgnoreCase));
        else if (hasRegion)
            query = query.Where(u =>
                string.Equals(u.Region, region, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.SecondaryRegion, region, StringComparison.OrdinalIgnoreCase));

        var all = query.ToList();
        for (int i = all.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (all[i], all[j]) = (all[j], all[i]);
        }
        return all.Skip(skip).Take(take).Select(dto => AugmentWithRank(dto, config.Ranks)).ToList();
    }

    public async Task<UserDto?> GetUserByIdAsync(string userId)
    {
        var config = await _appConfig.GetConfigAsync();
        var dto = MockDataStore.Users.FirstOrDefault(u => u.Id == userId);
        return dto is null ? null : AugmentWithRank(dto, config.Ranks);
    }

    public async Task<UserDto> UpdateUserAsync(string userId, UserDto user)
    {
        var existing = MockDataStore.Users.FirstOrDefault(u => u.Id == userId);
        if (existing is null)
            return user;

        existing.Name = user.Name;
        existing.Age = user.Age;
        existing.Bio = user.Bio;
        existing.Country = user.Country ?? string.Empty;
        existing.Region = user.Region ?? string.Empty;
        existing.SecondaryCountry = user.SecondaryCountry ?? string.Empty;
        existing.SecondaryRegion = user.SecondaryRegion ?? string.Empty;
        existing.Gender = user.Gender;
        existing.ProfileImage = user.ProfileImage;
        existing.Images = user.Images;
        existing.FavoriteSong = user.FavoriteSong;
        existing.Preferences = user.Preferences;
        existing.Settings = user.Settings;
        existing.InstagramHandle = user.InstagramHandle;
        existing.Prompts = user.Prompts;

        var config = await _appConfig.GetConfigAsync();
        return AugmentWithRank(existing, config.Ranks);
    }

    public Task IncrementCounterAsync(string userId, UserCounter counter, int delta = 1)
    {
        if (!MockDataStore.UserActivity.TryGetValue(userId, out var activity))
        {
            activity = new MockUserActivity();
            MockDataStore.UserActivity[userId] = activity;
        }
        switch (counter)
        {
            case UserCounter.ReplyCount:     activity.ReplyCount     += delta; break;
            case UserCounter.LikesReceived:  activity.LikesReceived  += delta; break;
            case UserCounter.EventsAttended: activity.EventsAttended += delta; break;
            case UserCounter.MatchCount:     activity.MatchCount     += delta; break;
        }
        return Task.CompletedTask;
    }

    public Task SetStaffRoleAsync(string userId, StaffRole role)
    {
        MockDataStore.UserStaffRoles[userId] = role;
        return Task.CompletedTask;
    }

    public Task SetRankOverrideAsync(string userId, UserRank? rank)
    {
        if (rank is null)
            MockDataStore.UserRankOverrides.Remove(userId);
        else
            MockDataStore.UserRankOverrides[userId] = rank.Value;
        return Task.CompletedTask;
    }

    public Task<(bool TelegramLinked, bool EmailVerified)> GetNotificationContactStatusAsync(string userId)
    {
        var user = MockDataStore.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null) return Task.FromResult((false, false));

        var telegramLinked = MockDataStore.AuthMethodsByUserId.TryGetValue(userId, out var methods)
                            && methods.Contains("telegram");
        var emailVerified = MockDataStore.EmailVerifiedUserIds.ContainsKey(userId);
        return Task.FromResult((telegramLinked, emailVerified));
    }

    private UserDto AugmentWithRank(UserDto dto, RankThresholds t)
    {
        var activity = MockDataStore.UserActivity.TryGetValue(dto.Id, out var a)
            ? a : new MockUserActivity();
        var staffRole = MockDataStore.UserStaffRoles.TryGetValue(dto.Id, out var sr)
            ? sr : StaffRole.None;
        MockDataStore.UserRankOverrides.TryGetValue(dto.Id, out var overridden);

        var fakeEntity = new UserEntity
        {
            ReplyCount = activity.ReplyCount,
            LikesReceived = activity.LikesReceived,
            EventsAttended = activity.EventsAttended,
            MatchCount = activity.MatchCount,
            RankOverride = MockDataStore.UserRankOverrides.ContainsKey(dto.Id)
                ? overridden.ToString().ToCamelCase() : null,
        };
        dto.Rank = RankCalculator.Compute(fakeEntity, t);
        dto.StaffRole = staffRole;
        return dto;
    }
}

internal static class StringCasing
{
    public static string ToCamelCase(this string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
