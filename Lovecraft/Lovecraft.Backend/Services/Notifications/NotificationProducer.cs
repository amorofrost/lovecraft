using System.Text.Json;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging;

namespace Lovecraft.Backend.Services.Notifications;

public class NotificationProducer : INotificationProducer
{
    private readonly INotificationService _notifications;
    private readonly INotificationPreferenceService _prefs;
    private readonly IPushSubscriptionService _push;
    private readonly IUserService _users;
    private readonly IInAppDispatcher _inApp;
    private readonly IWebPushDispatcher _webPush;
    private readonly IPresenceTracker _presence;
    private readonly NotificationDeduper _deduper;
    private readonly ILogger<NotificationProducer> _logger;

    public NotificationProducer(
        INotificationService notifications,
        INotificationPreferenceService prefs,
        IPushSubscriptionService push,
        IUserService users,
        IInAppDispatcher inApp,
        IWebPushDispatcher webPush,
        IPresenceTracker presence,
        NotificationDeduper deduper,
        ILogger<NotificationProducer> logger)
    {
        _notifications = notifications;
        _prefs = prefs;
        _push = push;
        _users = users;
        _inApp = inApp;
        _webPush = webPush;
        _presence = presence;
        _deduper = deduper;
        _logger = logger;
    }

    public async Task<NotificationDto?> ProduceAsync(
        string recipientUserId, NotificationType type, string? actorId,
        string payloadJson, string? sourceEventId, string? presenceGroup = null)
    {
        // 1. Self-action skip
        if (actorId == recipientUserId) return null;

        // 2. In-chat / in-topic suppression
        var effectiveGroup = presenceGroup ?? DerivePresenceGroup(type, payloadJson);
        if (effectiveGroup is not null && _presence.IsInGroup(effectiveGroup, recipientUserId))
            return null;

        // 3. Dedup window
        if (await _deduper.IsDuplicateAsync(recipientUserId, type, actorId, sourceEventId))
            return null;

        // 4. Channel resolution
        var prefs = await _prefs.GetPreferencesAsync(recipientUserId);
        var avail = await BuildAvailabilityAsync(recipientUserId);
        var channels = NotificationPolicy.ResolveChannels(prefs, type, avail);

        // 5. Write canonical row (always, even if no channels — bell is the inbox)
        var dto = await _notifications.CreateAsync(recipientUserId, type, actorId, payloadJson, sourceEventId);

        // 5b. Supersede older conversation-style notifications so the feed only
        //     shows the latest message per chat / latest reply per forum topic.
        //     Failures here are logged but never abort the produce — the new row
        //     is already persisted at this point.
        try { await SupersedeOlderAsync(recipientUserId, type, payloadJson, dto.Id); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to supersede older notifications for {Type} on {RecipientId}", type, recipientUserId); }

        // 6. Dispatch in-process channels or enqueue outbox for worker-dispatched channels
        var now = DateTime.UtcNow;
        foreach (var channel in channels)
        {
            if (channel == NotificationChannel.InApp)
            {
                try { await _inApp.DispatchAsync(recipientUserId, dto); }
                catch (Exception ex) { _logger.LogWarning(ex, "InApp dispatch failed for {NotificationId}", dto.Id); }
                continue;  // no outbox enqueue for in-process channels
            }
            if (channel == NotificationChannel.WebPush)
            {
                // Fire-and-forget: canonical notification row is already written; push delivery
                // is best-effort and must not block the API request thread.
                var capturedDto = dto;
                var capturedUserId = recipientUserId;
                _ = Task.Run(async () =>
                {
                    try { await _webPush.DispatchAsync(capturedUserId, capturedDto); }
                    catch (Exception ex) { _logger.LogWarning(ex, "WebPush dispatch failed for {NotificationId}", capturedDto.Id); }
                });
                continue;
            }
            // Telegram + Email: enqueue outbox for worker dispatch
            var frequencyKey = char.ToLowerInvariant(channel.ToString()[0]) + channel.ToString()[1..];
            var frequency = prefs.Frequency.TryGetValue(frequencyKey, out var f) ? f : NotificationFrequency.Immediate;
            var scheduledFor = ScheduleFor(now, frequency, prefs.DailyDigestHourUtc);
            try { await _notifications.EnqueueOutboxAsync(recipientUserId, dto.Id, channel, frequency, scheduledFor); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to enqueue outbox row for {Channel}/{NotificationId}", channel, dto.Id); }
        }

        return dto;
    }

    /// <summary>
    /// For MessageReceived, extracts chatId from the payload JSON and returns "chat-{chatId}".
    /// Returns null for all other types (no auto-suppression).
    /// </summary>
    private static string? DerivePresenceGroup(NotificationType type, string payloadJson)
    {
        if (type != NotificationType.MessageReceived) return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("chatId", out var chatIdProp))
            {
                var chatId = chatIdProp.GetString();
                if (!string.IsNullOrEmpty(chatId))
                    return $"chat-{chatId}";
            }
        }
        catch { /* malformed JSON — don't suppress */ }
        return null;
    }

    /// <summary>
    /// For conversation-style notification types (MessageReceived per chat,
    /// ForumReplyToThread per topic), hard-deletes every prior notification
    /// in the recipient's history that targets the same conversation. The
    /// just-written notification is exempt (by id). Other notification types
    /// are no-ops — likes, matches, broadcasts, events, etc. are NOT collapsed
    /// since each represents a distinct discrete event.
    /// </summary>
    private async Task SupersedeOlderAsync(
        string recipientUserId, NotificationType type, string payloadJson, string newNotificationId)
    {
        var key = ExtractConversationKey(type, payloadJson);
        if (key is null) return;

        // Most users have <50 active notifications; 200 gives generous headroom.
        var existing = await _notifications.ListAsync(recipientUserId, limit: 200, cursor: null);
        foreach (var n in existing)
        {
            if (n.Id == newNotificationId) continue;
            if (n.Type != type) continue;
            var otherKey = ExtractConversationKey(type, n.PayloadJson);
            if (otherKey != key) continue;
            try { await _notifications.RemoveAsync(recipientUserId, n.Id); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to remove superseded notification {Id}", n.Id); }
        }
    }

    /// <summary>
    /// Returns the conversation key (chatId for MessageReceived, topicId for
    /// ForumReplyToThread) extracted from payload JSON; null for any other type
    /// or when the expected field is missing/malformed.
    /// </summary>
    private static string? ExtractConversationKey(NotificationType type, string payloadJson)
    {
        var fieldName = type switch
        {
            NotificationType.MessageReceived    => "chatId",
            NotificationType.ForumReplyToThread => "topicId",
            _ => null,
        };
        if (fieldName is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty(fieldName, out var prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString();
                return string.IsNullOrEmpty(value) ? null : value;
            }
        }
        catch { /* malformed JSON — treat as no key */ }
        return null;
    }

    private async Task<ChannelAvailability> BuildAvailabilityAsync(string userId)
    {
        var status = await _users.GetNotificationContactStatusAsync(userId);
        var subCount = await _push.CountAsync(userId);
        return new ChannelAvailability
        {
            TelegramLinked    = status.TelegramLinked,
            EmailVerified     = status.EmailVerified,
            WebPushSubscribed = subCount > 0,
        };
    }

    private static DateTime ScheduleFor(DateTime now, NotificationFrequency frequency, int dailyHourUtc) => frequency switch
    {
        NotificationFrequency.Hourly => new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(1),
        NotificationFrequency.Daily  => NextDailySlot(now, dailyHourUtc),
        _ => now,
    };

    private static DateTime NextDailySlot(DateTime now, int hourUtc)
    {
        var today = new DateTime(now.Year, now.Month, now.Day, hourUtc, 0, 0, DateTimeKind.Utc);
        return today > now ? today : today.AddDays(1);
    }
}
