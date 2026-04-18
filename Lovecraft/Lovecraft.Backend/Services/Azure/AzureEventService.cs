using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
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
            var attendees = await GetAttendeeIdsAsync(eventId);
            return ToDto(response.Value, attendees);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> RegisterForEventAsync(string userId, string eventId)
    {
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
        };
    }

    private static EventVisibility ResolveVisibility(EventEntity entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.Visibility)
            && Enum.TryParse<EventVisibility>(entity.Visibility, ignoreCase: true, out var parsed))
            return parsed;

        return entity.IsSecret ? EventVisibility.SecretHidden : EventVisibility.Public;
    }
}
