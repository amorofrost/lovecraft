using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace Lovecraft.Backend.Services.Azure;

public class AzureAppConfigService : IAppConfigService
{
    private const string CacheKey = "appconfig:all";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly TableClient _table;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AzureAppConfigService> _logger;

    public AzureAppConfigService(
        TableServiceClient tableServiceClient,
        IMemoryCache cache,
        ILogger<AzureAppConfigService> logger)
    {
        _cache = cache;
        _logger = logger;
        _table = tableServiceClient.GetTableClient(TableNames.AppConfig);
        _table.CreateIfNotExistsAsync().GetAwaiter().GetResult();
    }

    public async Task<AppConfig> GetConfigAsync()
    {
        if (_cache.TryGetValue(CacheKey, out AppConfig? cached) && cached is not null)
            return cached;

        var rows = new List<AppConfigEntity>();
        await foreach (var row in _table.QueryAsync<AppConfigEntity>())
            rows.Add(row);

        var config = BuildConfig(rows);
        _cache.Set(CacheKey, config, CacheTtl);
        return config;
    }

    private AppConfig BuildConfig(IReadOnlyList<AppConfigEntity> rows)
    {
        var thresholds = rows
            .Where(r => r.PartitionKey == AppConfigEntity.PartitionRankThresholds)
            .ToDictionary(r => r.RowKey, r => r.Value, StringComparer.OrdinalIgnoreCase);
        var perms = rows
            .Where(r => r.PartitionKey == AppConfigEntity.PartitionPermissions)
            .ToDictionary(r => r.RowKey, r => r.Value, StringComparer.OrdinalIgnoreCase);

        int I(string key, int fallback) =>
            thresholds.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;
        string S(string key, string fallback) =>
            perms.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

        var d = RankThresholds.Defaults;
        var p = PermissionConfig.Defaults;
        return new AppConfig(
            new RankThresholds(
                ActiveReplies: I(AppConfigKeys.RankThresholdsKeys.ActiveReplies, d.ActiveReplies),
                ActiveLikes: I(AppConfigKeys.RankThresholdsKeys.ActiveLikes, d.ActiveLikes),
                ActiveEvents: I(AppConfigKeys.RankThresholdsKeys.ActiveEvents, d.ActiveEvents),
                FriendReplies: I(AppConfigKeys.RankThresholdsKeys.FriendReplies, d.FriendReplies),
                FriendLikes: I(AppConfigKeys.RankThresholdsKeys.FriendLikes, d.FriendLikes),
                FriendEvents: I(AppConfigKeys.RankThresholdsKeys.FriendEvents, d.FriendEvents),
                CrewReplies: I(AppConfigKeys.RankThresholdsKeys.CrewReplies, d.CrewReplies),
                CrewLikes: I(AppConfigKeys.RankThresholdsKeys.CrewLikes, d.CrewLikes),
                CrewEvents: I(AppConfigKeys.RankThresholdsKeys.CrewEvents, d.CrewEvents),
                CrewMatches: I(AppConfigKeys.RankThresholdsKeys.CrewMatches, d.CrewMatches)),
            new PermissionConfig(
                CreateTopic: S(AppConfigKeys.PermissionKeys.CreateTopic, p.CreateTopic),
                DeleteOwnReply: S(AppConfigKeys.PermissionKeys.DeleteOwnReply, p.DeleteOwnReply),
                DeleteAnyReply: S(AppConfigKeys.PermissionKeys.DeleteAnyReply, p.DeleteAnyReply),
                DeleteAnyTopic: S(AppConfigKeys.PermissionKeys.DeleteAnyTopic, p.DeleteAnyTopic),
                PinTopic: S(AppConfigKeys.PermissionKeys.PinTopic, p.PinTopic),
                BanUser: S(AppConfigKeys.PermissionKeys.BanUser, p.BanUser),
                AssignRole: S(AppConfigKeys.PermissionKeys.AssignRole, p.AssignRole),
                OverrideRank: S(AppConfigKeys.PermissionKeys.OverrideRank, p.OverrideRank),
                ManageEvents: S(AppConfigKeys.PermissionKeys.ManageEvents, p.ManageEvents),
                ManageBlog: S(AppConfigKeys.PermissionKeys.ManageBlog, p.ManageBlog),
                ManageStore: S(AppConfigKeys.PermissionKeys.ManageStore, p.ManageStore)));
    }
}
