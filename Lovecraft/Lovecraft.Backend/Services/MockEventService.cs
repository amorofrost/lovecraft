using Lovecraft.Common.DTOs.Admin;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.Enums;
using Lovecraft.Backend.MockData;

namespace Lovecraft.Backend.Services;

public class MockEventService : IEventService
{
    private readonly IUserService _userService;

    public MockEventService(IUserService userService)
    {
        _userService = userService;
    }

    public Task<List<EventDto>> GetEventsAsync()
    {
        var list = MockDataStore.Events.Where(e => !e.Archived).ToList();
        return Task.FromResult(list);
    }

    public Task<EventDto?> GetEventByIdAsync(string eventId)
    {
        var eventDto = MockDataStore.Events.FirstOrDefault(e => e.Id == eventId);
        if (eventDto is null || eventDto.Archived)
            return Task.FromResult<EventDto?>(null);
        return Task.FromResult<EventDto?>(eventDto);
    }

    public Task<List<EventDto>> GetEventsAdminAsync()
        => Task.FromResult(MockDataStore.Events.ToList());

    public Task<EventDto?> GetEventByIdAdminAsync(string eventId)
    {
        var eventDto = MockDataStore.Events.FirstOrDefault(e => e.Id == eventId);
        return Task.FromResult(eventDto);
    }

    public Task<EventDto> CreateEventAsync(AdminEventWriteDto dto)
    {
        var id = $"evt-{Guid.NewGuid():N}"[..16];
        var ev = new EventDto
        {
            Id = id,
            Title = dto.Title,
            Description = dto.Description,
            ImageUrl = dto.ImageUrl,
            Date = dto.Date,
            EndDate = dto.EndDate,
            Location = dto.Location,
            Capacity = dto.Capacity,
            Attendees = new List<string>(),
            Category = dto.Category,
            Price = dto.Price,
            Organizer = dto.Organizer,
            Visibility = dto.Visibility,
            IsSecret = dto.Visibility != EventVisibility.Public,
            ForumTopicId = null,
            Archived = dto.Archived,
        };
        MockDataStore.Events.Add(ev);
        return Task.FromResult(ev);
    }

    public Task<EventDto?> UpdateEventAsync(string eventId, AdminEventWriteDto dto)
    {
        var eventDto = MockDataStore.Events.FirstOrDefault(e => e.Id == eventId);
        if (eventDto is null)
            return Task.FromResult<EventDto?>(null);
        eventDto.Title = dto.Title;
        eventDto.Description = dto.Description;
        eventDto.ImageUrl = dto.ImageUrl;
        eventDto.Date = dto.Date;
        eventDto.EndDate = dto.EndDate;
        eventDto.Location = dto.Location;
        eventDto.Capacity = dto.Capacity;
        eventDto.Category = dto.Category;
        eventDto.Price = dto.Price;
        eventDto.Organizer = dto.Organizer;
        eventDto.Visibility = dto.Visibility;
        eventDto.IsSecret = dto.Visibility != EventVisibility.Public;
        eventDto.Archived = dto.Archived;
        return Task.FromResult<EventDto?>(eventDto);
    }

    public Task<bool> DeleteEventAsync(string eventId)
    {
        var ix = MockDataStore.Events.FindIndex(e => e.Id == eventId);
        if (ix < 0)
            return Task.FromResult(false);
        MockDataStore.Events.RemoveAt(ix);
        return Task.FromResult(true);
    }

    public Task<bool> SetEventArchivedAsync(string eventId, bool archived)
    {
        var eventDto = MockDataStore.Events.FirstOrDefault(e => e.Id == eventId);
        if (eventDto is null)
            return Task.FromResult(false);
        eventDto.Archived = archived;
        return Task.FromResult(true);
    }

    public async Task<List<EventAttendeeAdminDto>> GetEventAttendeesAsync(string eventId)
    {
        var list = new List<EventAttendeeAdminDto>();
        var ev = MockDataStore.Events.FirstOrDefault(e => e.Id == eventId);
        if (ev is null)
            return list;
        foreach (var uid in ev.Attendees)
        {
            var u = await _userService.GetUserByIdAsync(uid);
            list.Add(new EventAttendeeAdminDto(uid, u?.Name ?? uid));
        }
        return list;
    }

    public async Task<bool> RemoveEventAttendeeAsync(string eventId, string userId)
    {
        var eventDto = MockDataStore.Events.FirstOrDefault(e => e.Id == eventId);
        if (eventDto is null || !eventDto.Attendees.Contains(userId))
            return false;
        eventDto.Attendees.Remove(userId);
        try
        {
            await _userService.IncrementCounterAsync(userId, UserCounter.EventsAttended, -1);
        }
        catch
        {
            // ignore
        }
        return true;
    }

    public Task<bool> RegisterForEventAsync(string userId, string eventId)
    {
        var eventDto = MockDataStore.Events.FirstOrDefault(e => e.Id == eventId);
        if (eventDto != null && !eventDto.Archived && !eventDto.Attendees.Contains(userId))
        {
            eventDto.Attendees.Add(userId);
            _userService.IncrementCounterAsync(userId, UserCounter.EventsAttended).GetAwaiter().GetResult();
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<bool> UnregisterFromEventAsync(string userId, string eventId)
    {
        var eventDto = MockDataStore.Events.FirstOrDefault(e => e.Id == eventId);
        if (eventDto != null && eventDto.Attendees.Contains(userId))
        {
            eventDto.Attendees.Remove(userId);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task SetForumTopicIdAsync(string eventId, string forumTopicId)
    {
        var evt = MockDataStore.Events.FirstOrDefault(e => e.Id == eventId);
        if (evt != null)
            evt.ForumTopicId = forumTopicId;
        return Task.CompletedTask;
    }
}
