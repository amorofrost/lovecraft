using System.Collections.Concurrent;
using Lovecraft.Backend.MockData;

namespace Lovecraft.Backend.Services;

public class MockForumSubscriptionService : IForumSubscriptionService
{
    public Task SubscribeAsync(string userId, string topicId, string source)
    {
        var bucket = MockDataStore.ForumSubscriptions.GetOrAdd(topicId, _ => new HashSet<string>());
        lock (bucket)
        {
            bucket.Add(userId);
        }
        return Task.CompletedTask;
    }

    public Task<bool> UnsubscribeAsync(string userId, string topicId)
    {
        if (!MockDataStore.ForumSubscriptions.TryGetValue(topicId, out var bucket))
            return Task.FromResult(false);
        lock (bucket)
        {
            return Task.FromResult(bucket.Remove(userId));
        }
    }

    public Task<bool> IsSubscribedAsync(string userId, string topicId)
    {
        if (!MockDataStore.ForumSubscriptions.TryGetValue(topicId, out var bucket))
            return Task.FromResult(false);
        lock (bucket)
        {
            return Task.FromResult(bucket.Contains(userId));
        }
    }

    public Task<List<string>> GetSubscribersAsync(string topicId)
    {
        if (!MockDataStore.ForumSubscriptions.TryGetValue(topicId, out var bucket))
            return Task.FromResult(new List<string>());
        lock (bucket)
        {
            return Task.FromResult(bucket.ToList());
        }
    }
}
