namespace Lovecraft.Backend.Services;

/// <summary>
/// Tracks per-user subscriptions to forum topics. Topic authors are auto-subscribed
/// on topic creation; repliers are auto-subscribed when posting a reply; users may
/// also subscribe/unsubscribe manually via the API. ForumReplyToThread notifications
/// fan out only to current subscribers.
/// </summary>
public interface IForumSubscriptionService
{
    /// <summary>Idempotent: subscribing an already-subscribed user must not throw.</summary>
    Task SubscribeAsync(string userId, string topicId, string source);

    /// <summary>Returns false if the row didn't exist.</summary>
    Task<bool> UnsubscribeAsync(string userId, string topicId);

    Task<bool> IsSubscribedAsync(string userId, string topicId);

    Task<List<string>> GetSubscribersAsync(string topicId);
}
