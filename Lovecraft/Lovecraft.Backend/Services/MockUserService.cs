using System.Text.Json;
using Lovecraft.Backend.Helpers;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lovecraft.Backend.Services;

public class MockUserService : IUserService
{
    private readonly IAppConfigService _appConfig;
    private readonly Lazy<INotificationProducer>? _producer;
    private readonly ILogger<MockUserService> _logger;

    public MockUserService(
        IAppConfigService appConfig,
        Lazy<INotificationProducer>? producer = null,
        ILogger<MockUserService>? logger = null)
    {
        _appConfig = appConfig;
        _producer = producer;
        _logger = logger ?? NullLogger<MockUserService>.Instance;
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

    public async Task IncrementCounterAsync(string userId, UserCounter counter, int delta = 1)
    {
        if (!MockDataStore.UserActivity.TryGetValue(userId, out var activity))
        {
            activity = new MockUserActivity();
            MockDataStore.UserActivity[userId] = activity;
        }

        // Snapshot pre-increment counters for rank-up detection.
        var preReply   = activity.ReplyCount;
        var preLikes   = activity.LikesReceived;
        var preEvents  = activity.EventsAttended;
        var preMatches = activity.MatchCount;
        MockDataStore.UserRankOverrides.TryGetValue(userId, out var overrideRank);
        var preOverride = MockDataStore.UserRankOverrides.ContainsKey(userId)
            ? overrideRank.ToString().ToCamelCase()
            : null;

        switch (counter)
        {
            case UserCounter.ReplyCount:     activity.ReplyCount     += delta; break;
            case UserCounter.LikesReceived:  activity.LikesReceived  += delta; break;
            case UserCounter.EventsAttended: activity.EventsAttended += delta; break;
            case UserCounter.MatchCount:     activity.MatchCount     += delta; break;
        }

        await TryFireRankUpAsync(userId, preReply, preLikes, preEvents, preMatches, preOverride, activity);
    }

    private async Task TryFireRankUpAsync(
        string userId,
        int preReply, int preLikes, int preEvents, int preMatches, string? preOverride,
        MockUserActivity newActivity)
    {
        if (_producer is null) return;
        try
        {
            var oldEntity = new UserEntity
            {
                ReplyCount = preReply,
                LikesReceived = preLikes,
                EventsAttended = preEvents,
                MatchCount = preMatches,
                RankOverride = preOverride,
            };
            var newEntity = new UserEntity
            {
                ReplyCount = newActivity.ReplyCount,
                LikesReceived = newActivity.LikesReceived,
                EventsAttended = newActivity.EventsAttended,
                MatchCount = newActivity.MatchCount,
                RankOverride = preOverride,
            };

            var cfg = await _appConfig.GetConfigAsync();
            var oldRank = RankCalculator.Compute(oldEntity, cfg.Ranks);
            var newRank = RankCalculator.Compute(newEntity, cfg.Ranks);

            if (oldRank == newRank) return;

            var oldRankName = oldRank.ToString().ToCamelCase();
            var newRankName = newRank.ToString().ToCamelCase();
            var oldLevel = EffectiveLevel.Parse(oldRankName);
            var newLevel = EffectiveLevel.Parse(newRankName);
            if (newLevel <= oldLevel) return;

            var payload = JsonSerializer.Serialize(new
            {
                previousRank = oldRankName,
                newRank = newRankName,
            });
            await _producer.Value.ProduceAsync(
                userId,
                NotificationType.RankUp,
                actorId: null,
                payloadJson: payload,
                sourceEventId: $"rank-up-{userId}-{newRankName}",
                presenceGroup: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RankUp producer failed for {UserId}", userId);
        }
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

    public Task<string?> GetUserIdByTelegramIdAsync(string telegramUserId)
    {
        MockDataStore.UserTelegramIndex.TryGetValue(telegramUserId, out var userId);
        return Task.FromResult(userId);
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
