using Microsoft.Extensions.Caching.Memory;
using Lovecraft.Common.DTOs.Forum;

namespace Lovecraft.Backend.Services.Caching;

/// <summary>
/// Caching decorator for IForumService.
///
/// TTLs:
///   - Sections:            5 minutes  (structure rarely changes)
///   - Topics per section:  60 seconds (reply counts update; eventual consistency acceptable)
///   - Topic detail:        60 seconds
///   - Replies per topic:   30 seconds (new replies appear promptly)
///
/// Invalidation: CreateReplyAsync removes the affected topic detail and reply list immediately.
/// The section topics list (showing reply counts) is left to expire naturally within its TTL —
/// a slight reply-count lag on the list view is acceptable.
/// </summary>
public class CachingForumService : IForumService
{
    private readonly IForumService _inner;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan SectionsTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TopicsTtl   = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RepliesTtl  = TimeSpan.FromSeconds(30);

    private const string SectionsKey = "forum:sections";
    private static string TopicsKey(string sectionId)  => $"forum:topics:{sectionId}";
    private static string TopicKey(string topicId)      => $"forum:topic:{topicId}";
    private static string RepliesKey(string topicId)    => $"forum:replies:{topicId}";

    public CachingForumService(IForumService inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<List<ForumSectionDto>> GetSectionsAsync()
    {
        if (_cache.TryGetValue(SectionsKey, out List<ForumSectionDto>? cached) && cached is not null)
            return cached;

        var result = await _inner.GetSectionsAsync();
        _cache.Set(SectionsKey, result, SectionsTtl);
        return result;
    }

    public Task<List<EventDiscussionSectionDto>> GetEventDiscussionSectionsAsync(string userId, bool isElevated) =>
        _inner.GetEventDiscussionSectionsAsync(userId, isElevated);

    public Task<List<ForumTopicDto>?> GetEventDiscussionTopicsAsync(string userId, string eventId, bool isElevated) =>
        _inner.GetEventDiscussionTopicsAsync(userId, eventId, isElevated);

    public async Task<List<ForumTopicDto>> GetTopicsAsync(string sectionId)
    {
        var key = TopicsKey(sectionId);
        if (_cache.TryGetValue(key, out List<ForumTopicDto>? cached) && cached is not null)
            return cached;

        var result = await _inner.GetTopicsAsync(sectionId);
        _cache.Set(key, result, TopicsTtl);
        return result;
    }

    public async Task<ForumTopicDto?> GetTopicByIdAsync(string topicId)
    {
        var key = TopicKey(topicId);
        if (_cache.TryGetValue(key, out ForumTopicDto? cached))
            return cached;

        var result = await _inner.GetTopicByIdAsync(topicId);
        if (result is not null)
            _cache.Set(key, result, TopicsTtl);
        return result;
    }

    public async Task<List<ForumReplyDto>> GetRepliesAsync(string topicId)
    {
        var key = RepliesKey(topicId);
        if (_cache.TryGetValue(key, out List<ForumReplyDto>? cached) && cached is not null)
            return cached;

        var result = await _inner.GetRepliesAsync(topicId);
        _cache.Set(key, result, RepliesTtl);
        return result;
    }

    public async Task<ForumReplyDto> CreateReplyAsync(
        string topicId, string authorId, string authorName, string content, List<string>? imageUrls = null)
    {
        var result = await _inner.CreateReplyAsync(topicId, authorId, authorName, content, imageUrls);

        // Invalidate topic detail (reply count changed) and reply list (new reply added).
        // Topics-per-section list is left to expire naturally — acceptable eventual consistency.
        _cache.Remove(TopicKey(topicId));
        _cache.Remove(RepliesKey(topicId));

        return result;
    }

    public Task<ForumTopicDto> CreateEventTopicAsync(string eventId, string eventName)
        => _inner.CreateEventTopicAsync(eventId, eventName);

    public async Task<ForumTopicDto> CreateTopicAsync(
        string sectionId, string authorId, string authorName, string title, string content,
        bool? noviceVisible = null, bool? noviceCanReply = null)
    {
        var result = await _inner.CreateTopicAsync(
            sectionId, authorId, authorName, title, content, noviceVisible, noviceCanReply);
        _cache.Remove(TopicsKey(sectionId));
        _cache.Remove(SectionsKey);
        return result;
    }

    public async Task<ForumTopicDto?> UpdateTopicAsync(string topicId, UpdateTopicRequestDto update)
    {
        var result = await _inner.UpdateTopicAsync(topicId, update);

        // Invalidate topic detail (fields may have changed) and the section's topic list
        // (pin/lock state affects ordering and visibility). Reply list is unaffected.
        _cache.Remove(TopicKey(topicId));
        if (result is not null)
            _cache.Remove(TopicsKey(result.SectionId));

        return result;
    }
}
