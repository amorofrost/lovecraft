using Microsoft.Extensions.Caching.Memory;
using Lovecraft.Common.DTOs.Events;

namespace Lovecraft.Backend.Services.Caching;

/// <summary>
/// Caching decorator for IEventService. Wraps the real storage-backed service and adds
/// an in-process memory cache to avoid redundant Azure Table Storage round-trips.
///
/// TTL: 30 seconds — short enough to reflect registration changes promptly.
/// Invalidation: RegisterForEvent / UnregisterFromEvent remove the affected keys immediately.
/// </summary>
public class CachingEventService : IEventService
{
    private readonly IEventService _inner;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private const string AllKey = "events:all";
    private static string EventKey(string id) => $"events:{id}";

    public CachingEventService(IEventService inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<List<EventDto>> GetEventsAsync()
    {
        if (_cache.TryGetValue(AllKey, out List<EventDto>? cached) && cached is not null)
            return cached;

        var result = await _inner.GetEventsAsync();
        _cache.Set(AllKey, result, Ttl);
        return result;
    }

    public async Task<EventDto?> GetEventByIdAsync(string eventId)
    {
        var key = EventKey(eventId);
        if (_cache.TryGetValue(key, out EventDto? cached))
            return cached;

        var result = await _inner.GetEventByIdAsync(eventId);
        if (result is not null)
            _cache.Set(key, result, Ttl);
        return result;
    }

    public async Task<bool> RegisterForEventAsync(string userId, string eventId)
    {
        var result = await _inner.RegisterForEventAsync(userId, eventId);
        if (result)
        {
            _cache.Remove(AllKey);
            _cache.Remove(EventKey(eventId));
        }
        return result;
    }

    public async Task<bool> UnregisterFromEventAsync(string userId, string eventId)
    {
        var result = await _inner.UnregisterFromEventAsync(userId, eventId);
        // Always invalidate — even if the unregister returned false (e.g. already gone),
        // the list cache may reflect a stale attendee count.
        _cache.Remove(AllKey);
        _cache.Remove(EventKey(eventId));
        return result;
    }

    public Task SetForumTopicIdAsync(string eventId, string forumTopicId)
        => _inner.SetForumTopicIdAsync(eventId, forumTopicId);
}
