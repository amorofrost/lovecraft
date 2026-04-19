using Azure;
using Azure.Data.Tables;
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
    private readonly TableClient _interestedTable;
    private readonly IUserService _userService;
    private readonly ILogger<AzureEventService> _logger;

    public AzureEventService(
        TableServiceClient tableServiceClient,
        IUserService userService,
        ILogger<AzureEventService> logger)
    {
        _userService = userService;
        _logger = logger;
        _eventsTable = tableServiceClient.GetTableClient(TableNames.Events);
        _attendeesTable = tableServiceClient.GetTableClient(TableNames.EventAttendees);
        _interestedTable = tableServiceClient.GetTableClient(TableNames.EventInterested);

        Task.WhenAll(
            _eventsTable.CreateIfNotExistsAsync(),
            _attendeesTable.CreateIfNotExistsAsync(),
            _interestedTable.CreateIfNotExistsAsync()
        ).GetAwaiter().GetResult();
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
        return ToDto(entity, new List<string>(), new List<string>());
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

        var entity = new EventAttendeeEntity
        {
            PartitionKey = eventId,
            RowKey = userId,
            RegisteredAt = DateTime.UtcNow
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
        try
        {
            await _attendeesTable.DeleteEntityAsync(eventId, userId);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
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
        var escaped = userId.Replace("'", "''");
        var eventIds = new List<string>();
        await foreach (var row in _attendeesTable.QueryAsync<EventAttendeeEntity>(
                     filter: $"RowKey eq '{escaped}'"))
            eventIds.Add(row.PartitionKey);

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
