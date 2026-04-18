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

        Task.WhenAll(
            _eventsTable.CreateIfNotExistsAsync(),
            _attendeesTable.CreateIfNotExistsAsync()
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
            events.Add(ToDto(entity, attendees));
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
            return ToDto(response.Value, attendees);
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
            events.Add(ToDto(entity, attendees));
        }
        return events;
    }

    public async Task<EventDto?> GetEventByIdAdminAsync(string eventId)
    {
        try
        {
            var response = await _eventsTable.GetEntityAsync<EventEntity>("EVENTS", eventId);
            var attendees = await GetAttendeeIdsAsync(eventId);
            return ToDto(response.Value, attendees);
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
        return ToDto(entity, new List<string>());
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
            return ToDto(entity, attendees);
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

        await foreach (var row in _attendeesTable.QueryAsync<EventAttendeeEntity>(
                     filter: $"PartitionKey eq '{eventId.Replace("'", "''")}'"))
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
            await _userService.IncrementCounterAsync(userId, UserCounter.EventsAttended);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to increment {Counter} for user {UserId}",
                UserCounter.EventsAttended, userId);
        }
        return true;
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
        var attendees = new List<string>();
        await foreach (var entity in _attendeesTable.QueryAsync<EventAttendeeEntity>(
            filter: $"PartitionKey eq '{eventId}'"))
        {
            attendees.Add(entity.RowKey);
        }
        return attendees;
    }

    private static EventDto ToDto(EventEntity entity, List<string> attendees)
    {
        var visibility = ResolveVisibility(entity);
        return new EventDto
        {
            Id = entity.RowKey,
            Title = entity.Title,
            Description = entity.Description,
            ImageUrl = entity.ImageUrl,
            Date = entity.Date,
            EndDate = entity.EndDate,
            Location = entity.Location,
            Capacity = entity.Capacity,
            Attendees = attendees,
            Category = Enum.TryParse<EventCategory>(entity.Category, true, out var cat) ? cat : EventCategory.Other,
            Price = entity.Price.HasValue ? (decimal?)Convert.ToDecimal(entity.Price.Value) : null,
            Organizer = entity.Organizer,
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
        entity.Date = dto.Date;
        entity.EndDate = dto.EndDate;
        entity.Location = dto.Location;
        entity.Capacity = dto.Capacity;
        entity.Category = dto.Category.ToString();
        entity.Price = dto.Price.HasValue ? (double?)Convert.ToDouble(dto.Price.Value) : null;
        entity.Organizer = dto.Organizer;
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
