using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Admin;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Services.Azure;

public class AzureEventService : IEventService
{
    private readonly TableClient _eventsTable;
    private readonly TableClient _attendeesTable;
    private readonly TableClient _userAttendedEventsTable;
    private readonly TableClient _interestedTable;
    private readonly IUserService _userService;
    private readonly ILogger<AzureEventService> _logger;
    private readonly INotificationProducer? _producer;
    private readonly Task _reverseIndexBackfill;

    public AzureEventService(
        TableServiceClient tableServiceClient,
        IUserService userService,
        ILogger<AzureEventService> logger,
        INotificationProducer? producer = null)
    {
        _userService = userService;
        _logger = logger;
        _producer = producer;
        _eventsTable = tableServiceClient.GetTableClient(TableNames.Events);
        _attendeesTable = tableServiceClient.GetTableClient(TableNames.EventAttendees);
        _userAttendedEventsTable = tableServiceClient.GetTableClient(TableNames.UserAttendedEvents);
        _interestedTable = tableServiceClient.GetTableClient(TableNames.EventInterested);

        Task.WhenAll(
            _eventsTable.CreateIfNotExistsAsync(),
            _attendeesTable.CreateIfNotExistsAsync(),
            _userAttendedEventsTable.CreateIfNotExistsAsync(),
            _interestedTable.CreateIfNotExistsAsync()
        ).GetAwaiter().GetResult();

        // Existing rows in eventattendees (e.g. seeded data) need to be mirrored into the
        // user-partitioned reverse index. Run once in the background so startup isn't blocked;
        // GetEventsAttendedByUserAsync awaits this task before reading the reverse index so
        // results are correct even on the first request after deploy.
        _reverseIndexBackfill = Task.Run(BackfillUserAttendedEventsAsync);
    }

    private async Task BackfillUserAttendedEventsAsync()
    {
        try
        {
            await foreach (var att in _attendeesTable.QueryAsync<EventAttendeeEntity>())
            {
                var mirror = new UserAttendedEventEntity
                {
                    PartitionKey = att.RowKey,        // userId
                    RowKey = att.PartitionKey,        // eventId
                    RegisteredAt = att.RegisteredAt,
                };
                try
                {
                    await _userAttendedEventsTable.UpsertEntityAsync(mirror, TableUpdateMode.Replace);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Backfill: failed to mirror attendance user={UserId} event={EventId}",
                        att.RowKey, att.PartitionKey);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UserAttendedEvents reverse-index backfill failed");
        }
    }

    public async Task<List<EventDto>> GetEventsAsync()
    {
        var events = new List<EventDto>();
        await foreach (var entity in _eventsTable.QueryAsync<EventEntity>(filter: $"PartitionKey eq 'EVENTS'"))
        {
            if (entity.Archived)
                continue;
            var attendees = await GetAttendeeIdsAsync(entity.RowKey);
            var interested = await GetInterestedIdsAsync(entity.RowKey);
            events.Add(ToDto(entity, attendees, interested));
        }
        return events;
    }

    public async Task<EventDto?> GetEventByIdAsync(string eventId)
    {
        try
        {
            var response = await _eventsTable.GetEntityAsync<EventEntity>("EVENTS", eventId);
            if (response.Value.Archived)
                return null;
            var attendees = await GetAttendeeIdsAsync(eventId);
            var interested = await GetInterestedIdsAsync(eventId);
            return ToDto(response.Value, attendees, interested);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<EventDto>> GetEventsAdminAsync()
    {
        var events = new List<EventDto>();
        await foreach (var entity in _eventsTable.QueryAsync<EventEntity>(filter: $"PartitionKey eq 'EVENTS'"))
        {
            var attendees = await GetAttendeeIdsAsync(entity.RowKey);
            var interested = await GetInterestedIdsAsync(entity.RowKey);
            events.Add(ToDto(entity, attendees, interested));
        }
        return events;
    }

    public async Task<EventDto?> GetEventByIdAdminAsync(string eventId)
    {
        try
        {
            var response = await _eventsTable.GetEntityAsync<EventEntity>("EVENTS", eventId);
            var attendees = await GetAttendeeIdsAsync(eventId);
            var interested = await GetInterestedIdsAsync(eventId);
            return ToDto(response.Value, attendees, interested);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<EventDto> CreateEventAsync(AdminEventWriteDto dto)
    {
        var id = $"evt-{Guid.NewGuid():N}"[..16];
        var entity = new EventEntity
        {
            PartitionKey = "EVENTS",
            RowKey = id,
        };
        ApplyAdminWrite(entity, dto);
        await _eventsTable.AddEntityAsync(entity);
        var result = ToDto(entity, new List<string>(), new List<string>());

        // Fan out EventPublished notifications to all users for public events.
        // Per-user channel filtering is done by NotificationPolicy.ResolveChannels (Phase A);
        // default prefs have inApp=true / other channels=false unless user opted in.
        // NOTE: take: 10_000 mirrors BroadcastAudienceResolver — switch to paginated
        // bulk-list when the user table outgrows a single round-trip.
        if (_producer is not null && dto.Visibility == EventVisibility.Public)
        {
            try
            {
                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    eventId = result.Id,
                    eventTitle = result.Title,
                    eventDateUtc = result.Date.ToString("o"),
                });

                var allUsers = await _userService.GetUsersAsync(skip: 0, take: 10_000);
                foreach (var u in allUsers)
                {
                    try
                    {
                        await _producer.ProduceAsync(
                            u.Id,
                            NotificationType.EventPublished,
                            actorId: null,
                            payloadJson: payload,
                            sourceEventId: $"event-published-{result.Id}",
                            presenceGroup: null);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "EventPublished producer failed for {UserId} on event {EventId}",
                            u.Id, result.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "EventPublished fanout failed for event {EventId}", result.Id);
            }
        }

        return result;
    }

    public async Task<EventDto?> UpdateEventAsync(string eventId, AdminEventWriteDto dto)
    {
        try
        {
            var response = await _eventsTable.GetEntityAsync<EventEntity>("EVENTS", eventId);
            var entity = response.Value;
            ApplyAdminWrite(entity, dto);
            await _eventsTable.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
            var attendees = await GetAttendeeIdsAsync(eventId);
            var interested = await GetInterestedIdsAsync(eventId);
            return ToDto(entity, attendees, interested);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> DeleteEventAsync(string eventId)
    {
        try
        {
            await _eventsTable.GetEntityAsync<EventEntity>("EVENTS", eventId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }

        var escaped = eventId.Replace("'", "''");
        await foreach (var row in _attendeesTable.QueryAsync<EventAttendeeEntity>(
                     filter: $"PartitionKey eq '{escaped}'"))
        {
            try
            {
                await _attendeesTable.DeleteEntityAsync(row.PartitionKey, row.RowKey);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // ignore
            }
            // Mirror deletion into the user-partitioned reverse index.
            try
            {
                await _userAttendedEventsTable.DeleteEntityAsync(row.RowKey, row.PartitionKey);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // ignore
            }
        }

        await foreach (var row in _interestedTable.QueryAsync<EventInterestedEntity>(
                     filter: $"PartitionKey eq '{escaped}'"))
        {
            try
            {
                await _interestedTable.DeleteEntityAsync(row.PartitionKey, row.RowKey);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // ignore
            }
        }

        try
        {
            await _eventsTable.DeleteEntityAsync("EVENTS", eventId);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<bool> SetEventArchivedAsync(string eventId, bool archived)
    {
        try
        {
            var response = await _eventsTable.GetEntityAsync<EventEntity>("EVENTS", eventId);
            var entity = response.Value;
            entity.Archived = archived;
            await _eventsTable.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<List<EventAttendeeAdminDto>> GetEventAttendeesAsync(string eventId)
    {
        var list = new List<EventAttendeeAdminDto>();
        var ids = await GetAttendeeIdsAsync(eventId);
        foreach (var uid in ids)
        {
            var u = await _userService.GetUserByIdAsync(uid);
            list.Add(new EventAttendeeAdminDto(uid, u?.Name ?? uid));
        }
        return list;
    }

    public async Task<bool> RemoveEventAttendeeAsync(string eventId, string userId)
    {
        try
        {
            await _attendeesTable.DeleteEntityAsync(eventId, userId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }

        try
        {
            await _userAttendedEventsTable.DeleteEntityAsync(userId, eventId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // reverse-index row already gone — fine
        }

        try
        {
            await _userService.IncrementCounterAsync(userId, UserCounter.EventsAttended, -1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrement {Counter} for user {UserId}",
                UserCounter.EventsAttended, userId);
        }

        return true;
    }

    public async Task<bool> RegisterForEventAsync(string userId, string eventId)
    {
        try
        {
            var row = await _eventsTable.GetEntityAsync<EventEntity>("EVENTS", eventId);
            if (row.Value.Archived)
                return false;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var entity = new EventAttendeeEntity
        {
            PartitionKey = eventId,
            RowKey = userId,
            RegisteredAt = now
        };

        try
        {
            await _attendeesTable.AddEntityAsync(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Already registered — idempotent no-op, do not bump counter.
            return false;
        }

        // Mirror into the user-partitioned reverse index so "events attended by this user"
        // is a single-partition scan. Upsert is safe — if the row already exists from a
        // prior backfill or retry, we just refresh RegisteredAt.
        try
        {
            await _userAttendedEventsTable.UpsertEntityAsync(new UserAttendedEventEntity
            {
                PartitionKey = userId,
                RowKey = eventId,
                RegisteredAt = now,
            }, TableUpdateMode.Replace);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to mirror attendance into reverse index user={UserId} event={EventId}",
                userId, eventId);
        }

        try
        {
            await _interestedTable.DeleteEntityAsync(eventId, userId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // not interested
        }

        try
        {
            await _userService.IncrementCounterAsync(userId, UserCounter.EventsAttended);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to increment {Counter} for user {UserId}",
                UserCounter.EventsAttended, userId);
        }
        return true;
    }

    public async Task<bool> AddEventInterestAsync(string userId, string eventId)
    {
        try
        {
            var row = await _eventsTable.GetEntityAsync<EventEntity>("EVENTS", eventId);
            if (row.Value.Archived)
                return false;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }

        if ((await GetAttendeeIdsAsync(eventId)).Contains(userId))
            return false;

        var entity = new EventInterestedEntity
        {
            PartitionKey = eventId,
            RowKey = userId,
            InterestedAt = DateTime.UtcNow
        };

        try
        {
            await _interestedTable.AddEntityAsync(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false;
        }

        return true;
    }

    public async Task<bool> RemoveEventInterestAsync(string userId, string eventId)
    {
        try
        {
            await _interestedTable.DeleteEntityAsync(eventId, userId);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<bool> UnregisterFromEventAsync(string userId, string eventId)
    {
        bool removed;
        try
        {
            await _attendeesTable.DeleteEntityAsync(eventId, userId);
            removed = true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            removed = false;
        }

        // Always clean up the reverse-index row to keep it consistent, even when the
        // primary row was missing.
        try
        {
            await _userAttendedEventsTable.DeleteEntityAsync(userId, eventId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // already gone
        }

        return removed;
    }

    public async Task SetForumTopicIdAsync(string eventId, string forumTopicId)
    {
        try
        {
            var response = await _eventsTable.GetEntityAsync<EventEntity>("EVENTS", eventId);
            var entity = response.Value;
            entity.ForumTopicId = forumTopicId;
            await _eventsTable.UpdateEntityAsync(entity, entity.ETag);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Event not found — no-op
        }
    }

    private async Task<List<string>> GetAttendeeIdsAsync(string eventId)
    {
        var escaped = eventId.Replace("'", "''");
        var attendees = new List<string>();
        await foreach (var entity in _attendeesTable.QueryAsync<EventAttendeeEntity>(
            filter: $"PartitionKey eq '{escaped}'"))
        {
            attendees.Add(entity.RowKey);
        }
        return attendees;
    }

    private async Task<List<string>> GetInterestedIdsAsync(string eventId)
    {
        var escaped = eventId.Replace("'", "''");
        var list = new List<string>();
        await foreach (var entity in _interestedTable.QueryAsync<EventInterestedEntity>(
            filter: $"PartitionKey eq '{escaped}'"))
        {
            list.Add(entity.RowKey);
        }
        return list;
    }

    public async Task<List<EventDto>> GetEventsAttendedByUserAsync(string userId)
    {
        // Wait for the startup backfill to complete so the reverse index is authoritative.
        // After the first session this is effectively a no-op.
        try { await _reverseIndexBackfill.ConfigureAwait(false); }
        catch { /* logged in BackfillUserAttendedEventsAsync */ }

        var escaped = userId.Replace("'", "''");
        var eventIds = new List<string>();
        await foreach (var row in _userAttendedEventsTable.QueryAsync<UserAttendedEventEntity>(
                     filter: $"PartitionKey eq '{escaped}'"))
            eventIds.Add(row.RowKey);

        var result = new List<EventDto>();
        foreach (var eventId in eventIds)
        {
            var ev = await GetEventByIdAdminAsync(eventId);
            if (ev != null)
                result.Add(ev);
        }

        return result.OrderByDescending(e => e.Date).ToList();
    }

    public async Task<(List<string> PreviewUrls, int TotalCount)> GetUserEventBadgePreviewAsync(string userId)
    {
        var attended = await GetEventsAttendedByUserAsync(userId);
        var withBadges = attended
            .Where(e => !string.IsNullOrWhiteSpace(e.BadgeImageUrl))
            .OrderByDescending(e => e.Date)
            .ToList();
        var total = withBadges.Count;
        var preview = withBadges.Take(3).Select(e => e.BadgeImageUrl.Trim()).ToList();
        return (preview, total);
    }

    private static EventDto ToDto(EventEntity entity, List<string> attendees, List<string> interestedUserIds)
    {
        var visibility = ResolveVisibility(entity);
        return new EventDto
        {
            Id = entity.RowKey,
            Title = entity.Title,
            Description = entity.Description,
            ImageUrl = entity.ImageUrl,
            BadgeImageUrl = entity.BadgeImageUrl ?? string.Empty,
            Date = entity.Date,
            EndDate = entity.EndDate,
            Location = entity.Location,
            Capacity = entity.Capacity,
            Attendees = attendees,
            InterestedUserIds = interestedUserIds,
            Category = Enum.TryParse<EventCategory>(entity.Category, true, out var cat) ? cat : EventCategory.Other,
            Price = entity.Price ?? string.Empty,
            Organizer = entity.Organizer,
            ExternalUrl = entity.ExternalUrl ?? string.Empty,
            Visibility = visibility,
            IsSecret = visibility != EventVisibility.Public,
            ForumTopicId = entity.ForumTopicId,
            Archived = entity.Archived,
        };
    }

    private static void ApplyAdminWrite(EventEntity entity, AdminEventWriteDto dto)
    {
        var visibility = dto.Visibility;
        entity.Title = dto.Title;
        entity.Description = dto.Description;
        entity.ImageUrl = dto.ImageUrl;
        entity.BadgeImageUrl = dto.BadgeImageUrl ?? string.Empty;
        entity.Date = dto.Date;
        entity.EndDate = dto.EndDate;
        entity.Location = dto.Location;
        entity.Capacity = dto.Capacity;
        entity.Category = dto.Category.ToString();
        entity.Price = dto.Price?.Trim() ?? string.Empty;
        entity.Organizer = dto.Organizer;
        entity.ExternalUrl = dto.ExternalUrl?.Trim() ?? string.Empty;
        entity.Visibility = visibility.ToString();
        entity.IsSecret = visibility != EventVisibility.Public;
        entity.Archived = dto.Archived;
    }

    private static EventVisibility ResolveVisibility(EventEntity entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.Visibility)
            && Enum.TryParse<EventVisibility>(entity.Visibility, ignoreCase: true, out var parsed))
            return parsed;

        return entity.IsSecret ? EventVisibility.SecretHidden : EventVisibility.Public;
    }
}
