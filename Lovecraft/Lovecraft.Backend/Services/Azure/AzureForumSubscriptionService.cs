using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;

namespace Lovecraft.Backend.Services.Azure;

public class AzureForumSubscriptionService : IForumSubscriptionService
{
    private readonly TableClient _table;
    private readonly ILogger<AzureForumSubscriptionService> _logger;

    public AzureForumSubscriptionService(
        TableServiceClient tableSvc,
        ILogger<AzureForumSubscriptionService> logger)
    {
        _table = tableSvc.GetTableClient(TableNames.ForumTopicSubscriptions);
        _table.CreateIfNotExists();
        _logger = logger;
    }

    public async Task SubscribeAsync(string userId, string topicId, string source)
    {
        var entity = new ForumTopicSubscriptionEntity
        {
            PartitionKey = topicId,
            RowKey = userId,
            TopicId = topicId,
            UserId = userId,
            SubscribedAtUtc = DateTime.UtcNow,
            Source = source ?? "manual",
        };
        // Upsert keeps SubscribeAsync idempotent — re-subscribing is a no-op apart from
        // refreshing SubscribedAtUtc / Source.
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }

    public async Task<bool> UnsubscribeAsync(string userId, string topicId)
    {
        try
        {
            await _table.DeleteEntityAsync(topicId, userId);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<bool> IsSubscribedAsync(string userId, string topicId)
    {
        try
        {
            await _table.GetEntityAsync<ForumTopicSubscriptionEntity>(topicId, userId);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<List<string>> GetSubscribersAsync(string topicId)
    {
        var ids = new List<string>();
        await foreach (var e in _table.QueryAsync<ForumTopicSubscriptionEntity>(e => e.PartitionKey == topicId))
        {
            ids.Add(e.RowKey);
        }
        return ids;
    }
}
