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
        return Task.FromResult(MockDataStore.Events);
    }

    public Task<EventDto?> GetEventByIdAsync(string eventId)
    {
        var eventDto = MockDataStore.Events.FirstOrDefault(e => e.Id == eventId);
        return Task.FromResult(eventDto);
    }

    public Task<bool> RegisterForEventAsync(string userId, string eventId)
    {
        var eventDto = MockDataStore.Events.FirstOrDefault(e => e.Id == eventId);
        if (eventDto != null && !eventDto.Attendees.Contains(userId))
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
