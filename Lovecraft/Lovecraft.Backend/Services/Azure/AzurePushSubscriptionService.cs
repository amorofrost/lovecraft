using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Notifications;
using Microsoft.Extensions.Logging;

namespace Lovecraft.Backend.Services.Azure;

public class AzurePushSubscriptionService : IPushSubscriptionService
{
    private readonly TableClient _table;
    private readonly ILogger<AzurePushSubscriptionService> _logger;

    public AzurePushSubscriptionService(TableClient table, ILogger<AzurePushSubscriptionService> logger)
    {
        _table = table;
        _logger = logger;
    }

    public async Task<WebPushSubscriptionDto> SubscribeAsync(string userId, WebPushSubscriptionRequestDto request)
    {
        var deviceId = string.IsNullOrEmpty(request.DeviceId) ? Guid.NewGuid().ToString("N") : request.DeviceId;
        var now = DateTime.UtcNow;
        DateTime createdAt = now;
        try
        {
            var existing = await _table.GetEntityAsync<WebPushSubscriptionEntity>(userId, deviceId);
            createdAt = existing.Value.CreatedAtUtc;
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { /* new */ }

        var entity = new WebPushSubscriptionEntity
        {
            PartitionKey = userId,
            RowKey = deviceId,
            Endpoint = request.Endpoint,
            P256dh = request.P256dh,
            Auth = request.Auth,
            UserAgent = request.UserAgent,
            CreatedAtUtc = createdAt,
            LastSeenAtUtc = now,
        };
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        return ToDto(entity);
    }

    public async Task<List<WebPushSubscriptionDto>> ListAsync(string userId)
    {
        var list = new List<WebPushSubscriptionDto>();
        await foreach (var e in _table.QueryAsync<WebPushSubscriptionEntity>($"PartitionKey eq '{userId}'"))
            list.Add(ToDto(e));
        return list;
    }

    public async Task<int> CountAsync(string userId)
    {
        var count = 0;
        await foreach (var _ in _table.QueryAsync<WebPushSubscriptionEntity>($"PartitionKey eq '{userId}'", select: new[] { "RowKey" }))
            count++;
        return count;
    }

    public async Task<bool> UnsubscribeAsync(string userId, string deviceId)
    {
        try
        {
            await _table.DeleteEntityAsync(userId, deviceId);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    private static WebPushSubscriptionDto ToDto(WebPushSubscriptionEntity e) => new()
    {
        DeviceId = e.RowKey,
        Endpoint = e.Endpoint,
        P256dh = e.P256dh,
        Auth = e.Auth,
        UserAgent = e.UserAgent,
        CreatedAtUtc = e.CreatedAtUtc,
        LastSeenAtUtc = e.LastSeenAtUtc,
    };
}
