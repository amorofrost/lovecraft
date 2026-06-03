using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Notifications;
using Microsoft.Extensions.Logging;

namespace Lovecraft.Backend.Services.Azure;

public class AzureFcmSubscriptionService : IFcmSubscriptionService
{
    private readonly TableClient _table;
    private readonly ILogger<AzureFcmSubscriptionService> _logger;

    public AzureFcmSubscriptionService(TableClient table, ILogger<AzureFcmSubscriptionService> logger)
    {
        _table = table;
        _logger = logger;
    }

    public async Task<FcmSubscriptionDto> RegisterAsync(string userId, FcmRegisterRequestDto request)
    {
        var deviceId = string.IsNullOrEmpty(request.DeviceId) ? Guid.NewGuid().ToString("N") : request.DeviceId;
        var now = DateTime.UtcNow;
        DateTime createdAt = now;
        try
        {
            var existing = await _table.GetEntityAsync<FcmSubscriptionEntity>(userId, deviceId);
            createdAt = existing.Value.CreatedAtUtc;
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { /* new */ }

        var entity = new FcmSubscriptionEntity
        {
            PartitionKey = userId,
            RowKey = deviceId,
            Token = request.Token,
            Platform = string.IsNullOrEmpty(request.Platform) ? "android" : request.Platform,
            DeviceModel = request.DeviceModel,
            CreatedAtUtc = createdAt,
            LastSeenAtUtc = now,
        };
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        return ToDto(entity);
    }

    public async Task<List<FcmSubscriptionDto>> ListAsync(string userId)
    {
        var list = new List<FcmSubscriptionDto>();
        await foreach (var e in _table.QueryAsync<FcmSubscriptionEntity>($"PartitionKey eq '{userId}'"))
            list.Add(ToDto(e));
        return list;
    }

    public async Task<int> CountAsync(string userId)
    {
        var count = 0;
        await foreach (var _ in _table.QueryAsync<FcmSubscriptionEntity>($"PartitionKey eq '{userId}'", select: new[] { "RowKey" }))
            count++;
        return count;
    }

    public async Task<bool> UnregisterAsync(string userId, string deviceId)
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

    private static FcmSubscriptionDto ToDto(FcmSubscriptionEntity e) => new()
    {
        DeviceId = e.RowKey,
        Token = e.Token,
        Platform = e.Platform,
        DeviceModel = e.DeviceModel,
        CreatedAtUtc = e.CreatedAtUtc,
        LastSeenAtUtc = e.LastSeenAtUtc,
    };
}
