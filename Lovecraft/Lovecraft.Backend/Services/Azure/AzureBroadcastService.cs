using System.Text.Json;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Notifications;

namespace Lovecraft.Backend.Services.Azure;

public class AzureBroadcastService : IBroadcastService
{
    private readonly TableClient _table;

    public AzureBroadcastService(TableServiceClient tableSvc)
    {
        _table = tableSvc.GetTableClient(TableNames.Broadcasts);
        _table.CreateIfNotExists();
    }

    public async Task<BroadcastDto> CreateAsync(CreateBroadcastRequestDto request, string issuedByUserId)
    {
        var now = DateTime.UtcNow;
        var id = $"bc-{Guid.NewGuid():N}".Substring(0, 16);
        var entity = new BroadcastEntity
        {
            PartitionKey = "BROADCAST",
            RowKey = BroadcastEntity.BuildRowKey(now, id),
            Id = id,
            Title = request.Title,
            Body = request.Body,
            Link = request.Link,
            AudienceJson = JsonSerializer.Serialize(request.Audience),
            IssuedByUserId = issuedByUserId,
            IssuedAtUtc = now,
            EstimatedRecipients = 0,
            DispatchedCount = 0,
            Status = "pending",
            CompletedAtUtc = null,
        };
        await _table.AddEntityAsync(entity);
        return ToDto(entity);
    }

    public async Task<BroadcastDto?> GetByIdAsync(string broadcastId)
    {
        var results = _table.QueryAsync<BroadcastEntity>(b => b.PartitionKey == "BROADCAST" && b.Id == broadcastId);
        await foreach (var e in results) return ToDto(e);
        return null;
    }

    public async Task<List<BroadcastDto>> ListAsync(int limit = 50)
    {
        var list = new List<BroadcastDto>();
        var results = _table.QueryAsync<BroadcastEntity>(b => b.PartitionKey == "BROADCAST", maxPerPage: limit);
        await foreach (var e in results)
        {
            list.Add(ToDto(e));
            if (list.Count >= limit) break;
        }
        return list;
    }

    public async Task SetEstimatedRecipientsAsync(string broadcastId, int count)
    {
        var entity = await FindEntityAsync(broadcastId);
        if (entity is null) return;
        entity.EstimatedRecipients = count;
        await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
    }

    public async Task SetCompletedAsync(string broadcastId, int dispatchedCount, DateTime completedAtUtc)
    {
        var entity = await FindEntityAsync(broadcastId);
        if (entity is null) return;
        entity.DispatchedCount = dispatchedCount;
        entity.Status = "completed";
        entity.CompletedAtUtc = completedAtUtc;
        await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
    }

    private async Task<BroadcastEntity?> FindEntityAsync(string broadcastId)
    {
        var results = _table.QueryAsync<BroadcastEntity>(b => b.PartitionKey == "BROADCAST" && b.Id == broadcastId);
        await foreach (var e in results) return e;
        return null;
    }

    private static BroadcastDto ToDto(BroadcastEntity e) => new()
    {
        Id = e.Id,
        Title = e.Title,
        Body = e.Body,
        Link = e.Link,
        Audience = JsonSerializer.Deserialize<BroadcastAudienceDto>(e.AudienceJson) ?? new("all", null),
        IssuedByUserId = e.IssuedByUserId,
        IssuedAtUtc = e.IssuedAtUtc,
        EstimatedRecipients = e.EstimatedRecipients,
        DispatchedCount = e.DispatchedCount,
        Status = e.Status,
        CompletedAtUtc = e.CompletedAtUtc,
    };
}
