using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.Enums;
using Lovecraft.Common.Models;
using Lovecraft.Backend.Services;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize] // Require authentication
public class EventsController : ControllerBase
{
    private readonly IEventService _eventService;
    private readonly IForumService _forumService;
    private readonly IEventInviteService _eventInvites;
    private readonly ILogger<EventsController> _logger;

    public EventsController(
        IEventService eventService,
        IForumService forumService,
        IEventInviteService eventInvites,
        ILogger<EventsController> logger)
    {
        _eventService = eventService;
        _forumService = forumService;
        _eventInvites = eventInvites;
        _logger = logger;
    }

    /// <summary>
    /// Get all events (secret hidden events omitted unless attendee/staff; secret teaser shown redacted for non-attendees).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<EventDto>>>> GetEvents()
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<List<EventDto>>.ErrorResponse("UNAUTHORIZED", "Not authenticated"));

            var events = await _eventService.GetEventsAsync();
            var staff = User.FindFirst("staffRole")?.Value ?? "none";
            var isElevated = staff is "moderator" or "admin";

            var filtered = new List<EventDto>();
            foreach (var e in events)
            {
                if (e.Visibility == EventVisibility.SecretHidden)
                {
                    if (isElevated || e.Attendees.Contains(userId))
                        filtered.Add(e);
                    continue;
                }

                if (e.Visibility == EventVisibility.SecretTeaser && !isElevated && !e.Attendees.Contains(userId))
                {
                    filtered.Add(ToTeaser(e));
                    continue;
                }

                filtered.Add(e);
            }

            return Ok(ApiResponse<List<EventDto>>.SuccessResponse(filtered));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting events");
            return StatusCode(500, ApiResponse<List<EventDto>>.ErrorResponse("INTERNAL_ERROR", "Failed to get events"));
        }
    }

    /// <summary>
    /// Get event by ID. Optional <paramref name="inviteCode"/> unlocks secret events when valid.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<EventDto>>> GetEvent(string id, [FromQuery] string? code = null)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<EventDto>.ErrorResponse("UNAUTHORIZED", "Not authenticated"));

            var eventDto = await _eventService.GetEventByIdAsync(id);
            if (eventDto == null)
            {
                return NotFound(ApiResponse<EventDto>.ErrorResponse("NOT_FOUND", "Event not found"));
            }

            var staff = User.FindFirst("staffRole")?.Value ?? "none";
            var isElevated = staff is "moderator" or "admin";

            if (!await CanViewFullEventAsync(eventDto, userId, code, isElevated))
            {
                if (eventDto.Visibility == EventVisibility.SecretTeaser)
                    return Ok(ApiResponse<EventDto>.SuccessResponse(ToTeaser(eventDto)));
                return NotFound(ApiResponse<EventDto>.ErrorResponse("NOT_FOUND", "Event not found"));
            }

            if (string.IsNullOrEmpty(eventDto.ForumTopicId))
            {
                var topic = await _forumService.CreateEventTopicAsync(id, eventDto.Title);
                await _eventService.SetForumTopicIdAsync(id, topic.Id);
                eventDto.ForumTopicId = topic.Id;
            }

            return Ok(ApiResponse<EventDto>.SuccessResponse(eventDto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting event {EventId}", id);
            return StatusCode(500, ApiResponse<EventDto>.ErrorResponse("INTERNAL_ERROR", "Failed to get event"));
        }
    }

    private async Task<bool> CanViewFullEventAsync(EventDto e, string userId, string? inviteCode, bool isElevated)
    {
        if (e.Visibility == EventVisibility.Public)
            return true;
        if (isElevated || e.Attendees.Contains(userId))
            return true;
        if (!string.IsNullOrWhiteSpace(inviteCode))
        {
            var v = await _eventInvites.ValidatePlainCodeAsync(inviteCode);
            if (v is not null && v.EventId == e.Id)
                return true;
        }
        return false;
    }

    private static EventDto ToTeaser(EventDto full) => new()
    {
        Id = full.Id,
        Title = full.Title,
        Description = string.Empty,
        ImageUrl = full.ImageUrl,
        Date = full.Date,
        EndDate = full.EndDate,
        Location = string.Empty,
        Capacity = full.Capacity,
        Attendees = new List<string>(),
        InterestedUserIds = new List<string>(),
        Category = full.Category,
        Price = string.Empty,
        Organizer = full.Organizer,
        ExternalUrl = full.ExternalUrl,
        Visibility = EventVisibility.SecretTeaser,
        IsSecret = true,
        ForumTopicId = null,
    };

    /// <summary>
    /// Register as an attendee — requires a valid per-event invite code (moderators/admins may omit).
    /// </summary>
    [HttpPost("{id}/register")]
    public async Task<ActionResult<ApiResponse<bool>>> RegisterForEvent(string id, [FromBody] RegisterForEventRequestDto? body)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<bool>.ErrorResponse("UNAUTHORIZED", "Not authenticated"));

            var staff = User.FindFirst("staffRole")?.Value ?? "none";
            var isElevated = staff is "moderator" or "admin";

            if (!isElevated)
            {
                if (string.IsNullOrWhiteSpace(body?.InviteCode))
                    return BadRequest(ApiResponse<bool>.ErrorResponse("INVITE_REQUIRED", "An event invite code is required to attend"));
                var v = await _eventInvites.ValidatePlainCodeAsync(body!.InviteCode!);
                if (v is null || v.EventId != id)
                    return BadRequest(ApiResponse<bool>.ErrorResponse("INVALID_INVITE_CODE", "Invalid or expired invite code for this event"));
            }

            var result = await _eventService.RegisterForEventAsync(userId, id);

            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("REGISTRATION_FAILED", "Failed to register for event"));
            }

            if (!string.IsNullOrWhiteSpace(body?.InviteCode))
            {
                var v = await _eventInvites.ValidatePlainCodeAsync(body.InviteCode);
                if (v is not null && v.EventId == id)
                    await _eventInvites.IncrementEventAttendanceClaimCountAsync(body.InviteCode);
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering for event {EventId}", id);
            return StatusCode(500, ApiResponse<bool>.ErrorResponse("INTERNAL_ERROR", "Failed to register for event"));
        }
    }

    /// <summary>Mark the current user as interested (does not add to attendees).</summary>
    [HttpPost("{id}/interest")]
    public async Task<ActionResult<ApiResponse<bool>>> AddInterest(string id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(ApiResponse<bool>.ErrorResponse("UNAUTHORIZED", "Not authenticated"));

        var ev = await _eventService.GetEventByIdAsync(id);
        if (ev is null)
            return NotFound(ApiResponse<bool>.ErrorResponse("NOT_FOUND", "Event not found"));

        var staff = User.FindFirst("staffRole")?.Value ?? "none";
        var isElevated = staff is "moderator" or "admin";
        if (!IsEventListedForUser(ev, userId, isElevated))
            return NotFound(ApiResponse<bool>.ErrorResponse("NOT_FOUND", "Event not found"));

        var ok = await _eventService.AddEventInterestAsync(userId, id);
        if (!ok)
            return BadRequest(ApiResponse<bool>.ErrorResponse("INTEREST_FAILED", "Could not save interest"));
        return Ok(ApiResponse<bool>.SuccessResponse(true));
    }

    /// <summary>Remove the current user from the interested list.</summary>
    [HttpDelete("{id}/interest")]
    public async Task<ActionResult<ApiResponse<bool>>> RemoveInterest(string id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(ApiResponse<bool>.ErrorResponse("UNAUTHORIZED", "Not authenticated"));

        var ok = await _eventService.RemoveEventInterestAsync(userId, id);
        if (!ok)
            return BadRequest(ApiResponse<bool>.ErrorResponse("INTEREST_REMOVE_FAILED", "Not marked as interested"));
        return Ok(ApiResponse<bool>.SuccessResponse(true));
    }

    /// <summary>Same visibility as GET /events list (public, teaser, attended secret, or staff).</summary>
    private static bool IsEventListedForUser(EventDto e, string userId, bool isElevated)
    {
        if (isElevated) return true;
        if (e.Visibility == EventVisibility.SecretHidden)
            return e.Attendees.Contains(userId);
        return true;
    }

    /// <summary>
    /// Unregister from an event
    /// </summary>
    [HttpDelete("{id}/register")]
    public async Task<ActionResult<ApiResponse<bool>>> UnregisterFromEvent(string id)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<bool>.ErrorResponse("UNAUTHORIZED", "Not authenticated"));

            var result = await _eventService.UnregisterFromEventAsync(userId, id);
            
            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("UNREGISTRATION_FAILED", "Failed to unregister from event"));
            }
            
            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unregistering from event {EventId}", id);
            return StatusCode(500, ApiResponse<bool>.ErrorResponse("INTERNAL_ERROR", "Failed to unregister from event"));
        }
    }
}
