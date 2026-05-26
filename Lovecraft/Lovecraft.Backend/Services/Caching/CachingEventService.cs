using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Lovecraft.Common.DTOs.Admin;
using Lovecraft.Common.DTOs.Events;

namespace Lovecraft.Backend.Services.Caching;

/// <summary>
/// Caching decorator for IEventService. Wraps the real storage-backed service and adds
/// an in-process memory cache to avoid redundant Azure Table Storage round-trips.
///
/// TTL: 30 seconds — short enough to reflect registration changes promptly.
/// Invalidation: RegisterForEvent / UnregisterFromEvent remove the affected keys immediately.
///
/// Per-user badge previews (GetUserEventBadgePreviewAsync) are cached with a longer
/// 5-minute TTL because each cache miss triggers a cross-partition attendees scan plus
/// N event point-reads, and the forum replies endpoint hits this once per distinct author.
/// </summary>
public class CachingEventService : IEventService
{
    private readonly IEventService _inner;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BadgePreviewTtl = TimeSpan.FromMinutes(5);
    private const string AllKey = "events:all";
    private static string EventKey(string id) => $"events:{id}";
    private static string BadgePreviewKey(string userId) => $"events:badge-preview:{userId}";

    // Swap-on-write token used to invalidate every cached badge preview at once when an
    // admin-side change to an event could affect any user's preview (badge URL changed,
    // event archived/deleted). Per-user changes invalidate just that user's key.
    private CancellationTokenSource _badgePreviewReset = new();

    public CachingEventService(IEventService inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    private void InvalidateAllBadgePreviews()
    {
        var old = Interlocked.Exchange(ref _badgePreviewReset, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
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
            _cache.Remove(BadgePreviewKey(userId));
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
        _cache.Remove(BadgePreviewKey(userId));
        return result;
    }

    public async Task<bool> AddEventInterestAsync(string userId, string eventId)
    {
        var result = await _inner.AddEventInterestAsync(userId, eventId);
        if (result)
        {
            _cache.Remove(AllKey);
            _cache.Remove(EventKey(eventId));
        }
        return result;
    }

    public async Task<bool> RemoveEventInterestAsync(string userId, string eventId)
    {
        var result = await _inner.RemoveEventInterestAsync(userId, eventId);
        if (result)
        {
            _cache.Remove(AllKey);
            _cache.Remove(EventKey(eventId));
        }
        return result;
    }

    public Task SetForumTopicIdAsync(string eventId, string forumTopicId)
        => _inner.SetForumTopicIdAsync(eventId, forumTopicId);

    public Task<List<EventDto>> GetEventsAdminAsync() => _inner.GetEventsAdminAsync();

    public Task<EventDto?> GetEventByIdAdminAsync(string eventId) => _inner.GetEventByIdAdminAsync(eventId);

    public async Task<EventDto> CreateEventAsync(AdminEventWriteDto dto)
    {
        var result = await _inner.CreateEventAsync(dto);
        _cache.Remove(AllKey);
        _cache.Remove(EventKey(result.Id));
        return result;
    }

    public async Task<EventDto?> UpdateEventAsync(string eventId, AdminEventWriteDto dto)
    {
        var result = await _inner.UpdateEventAsync(eventId, dto);
        _cache.Remove(AllKey);
        _cache.Remove(EventKey(eventId));
        // BadgeImageUrl may have changed — wipe all per-user badge previews.
        InvalidateAllBadgePreviews();
        return result;
    }

    public async Task<bool> DeleteEventAsync(string eventId)
    {
        var ok = await _inner.DeleteEventAsync(eventId);
        if (ok)
        {
            _cache.Remove(AllKey);
            _cache.Remove(EventKey(eventId));
            InvalidateAllBadgePreviews();
        }
        return ok;
    }

    public async Task<bool> SetEventArchivedAsync(string eventId, bool archived)
    {
        var ok = await _inner.SetEventArchivedAsync(eventId, archived);
        if (ok)
        {
            _cache.Remove(AllKey);
            _cache.Remove(EventKey(eventId));
            InvalidateAllBadgePreviews();
        }
        return ok;
    }

    public async Task<List<EventAttendeeAdminDto>> GetEventAttendeesAsync(string eventId)
        => await _inner.GetEventAttendeesAsync(eventId);

    public async Task<bool> RemoveEventAttendeeAsync(string eventId, string userId)
    {
        var ok = await _inner.RemoveEventAttendeeAsync(eventId, userId);
        if (ok)
        {
            _cache.Remove(AllKey);
            _cache.Remove(EventKey(eventId));
            _cache.Remove(BadgePreviewKey(userId));
        }
        return ok;
    }

    public Task<List<EventDto>> GetEventsAttendedByUserAsync(string userId) =>
        _inner.GetEventsAttendedByUserAsync(userId);

    public async Task<(List<string> PreviewUrls, int TotalCount)> GetUserEventBadgePreviewAsync(string userId)
    {
        var key = BadgePreviewKey(userId);
        if (_cache.TryGetValue<(List<string> PreviewUrls, int TotalCount)>(key, out var cached))
            return cached;

        var result = await _inner.GetUserEventBadgePreviewAsync(userId);

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = BadgePreviewTtl,
        };
        // Tie this entry to the global reset token so admin-side event changes can wipe
        // every per-user preview in O(1).
        options.AddExpirationToken(new CancellationChangeToken(_badgePreviewReset.Token));
        _cache.Set(key, result, options);
        return result;
    }
}
