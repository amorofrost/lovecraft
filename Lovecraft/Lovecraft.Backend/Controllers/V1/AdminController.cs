using System.Security.Claims;
using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Admin;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
[RequireStaffRole("admin")]
public class AdminController : ControllerBase
{
    private readonly IAppConfigService _appConfig;
    private readonly IEventInviteService _eventInvites;
    private readonly IEventService _events;
    private readonly IForumService _forum;

    public AdminController(
        IAppConfigService appConfig,
        IEventInviteService eventInvites,
        IEventService events,
        IForumService forum)
    {
        _appConfig = appConfig;
        _eventInvites = eventInvites;
        _events = events;
        _forum = forum;
    }

    [HttpGet("events")]
    public async Task<ActionResult<ApiResponse<List<EventDto>>>> GetEvents()
    {
        var list = await _events.GetEventsAdminAsync();
        return Ok(ApiResponse<List<EventDto>>.SuccessResponse(list));
    }

    [HttpGet("events/{eventId}")]
    public async Task<ActionResult<ApiResponse<EventDto>>> GetEvent(string eventId)
    {
        var e = await _events.GetEventByIdAdminAsync(eventId);
        if (e is null)
            return NotFound(ApiResponse<EventDto>.ErrorResponse("NOT_FOUND", "Event not found"));
        return Ok(ApiResponse<EventDto>.SuccessResponse(e));
    }

    [HttpPost("events")]
    public async Task<ActionResult<ApiResponse<EventDto>>> CreateEvent([FromBody] AdminEventWriteDto dto)
    {
        var e = await _events.CreateEventAsync(dto);
        return Ok(ApiResponse<EventDto>.SuccessResponse(e));
    }

    [HttpPut("events/{eventId}")]
    public async Task<ActionResult<ApiResponse<EventDto>>> UpdateEvent(string eventId, [FromBody] AdminEventWriteDto dto)
    {
        var e = await _events.UpdateEventAsync(eventId, dto);
        if (e is null)
            return NotFound(ApiResponse<EventDto>.ErrorResponse("NOT_FOUND", "Event not found"));
        return Ok(ApiResponse<EventDto>.SuccessResponse(e));
    }

    [HttpDelete("events/{eventId}")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteEvent(string eventId)
    {
        await _forum.DeleteTopicsForEventAsync(eventId);
        await _eventInvites.DeleteAllInvitesForEventAsync(eventId);
        var ok = await _events.DeleteEventAsync(eventId);
        if (!ok)
            return NotFound(ApiResponse<object?>.ErrorResponse("NOT_FOUND", "Event not found"));
        return Ok(ApiResponse<object?>.SuccessResponse(null));
    }

    [HttpPost("events/{eventId}/archive")]
    public async Task<ActionResult<ApiResponse<bool>>> SetArchive(string eventId, [FromBody] ArchiveEventRequestDto body)
    {
        var ok = await _events.SetEventArchivedAsync(eventId, body.Archived);
        if (!ok)
            return NotFound(ApiResponse<bool>.ErrorResponse("NOT_FOUND", "Event not found"));
        return Ok(ApiResponse<bool>.SuccessResponse(true));
    }

    [HttpGet("events/{eventId}/attendees")]
    public async Task<ActionResult<ApiResponse<List<EventAttendeeAdminDto>>>> GetAttendees(string eventId)
    {
        var e = await _events.GetEventByIdAdminAsync(eventId);
        if (e is null)
            return NotFound(ApiResponse<List<EventAttendeeAdminDto>>.ErrorResponse("NOT_FOUND", "Event not found"));
        var list = await _events.GetEventAttendeesAsync(eventId);
        return Ok(ApiResponse<List<EventAttendeeAdminDto>>.SuccessResponse(list));
    }

    [HttpDelete("events/{eventId}/attendees/{userId}")]
    public async Task<ActionResult<ApiResponse<bool>>> RemoveAttendee(string eventId, string userId)
    {
        var e = await _events.GetEventByIdAdminAsync(eventId);
        if (e is null)
            return NotFound(ApiResponse<bool>.ErrorResponse("NOT_FOUND", "Event not found"));
        var ok = await _events.RemoveEventAttendeeAsync(eventId, userId);
        if (!ok)
            return NotFound(ApiResponse<bool>.ErrorResponse("NOT_FOUND", "Attendee not found"));
        return Ok(ApiResponse<bool>.SuccessResponse(true));
    }

    [HttpGet("events/{eventId}/forum-topics")]
    public async Task<ActionResult<ApiResponse<List<ForumTopicDto>>>> GetEventForumTopics(string eventId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(ApiResponse<List<ForumTopicDto>>.ErrorResponse("UNAUTHORIZED", "Not authenticated"));

        var topics = await _forum.GetEventDiscussionTopicsAsync(userId, eventId, isElevated: true);
        if (topics is null)
            return NotFound(ApiResponse<List<ForumTopicDto>>.ErrorResponse("NOT_FOUND", "Event not found"));
        return Ok(ApiResponse<List<ForumTopicDto>>.SuccessResponse(topics));
    }

    [HttpPost("events/{eventId}/forum-topics")]
    public async Task<ActionResult<ApiResponse<ForumTopicDto>>> CreateEventForumTopic(
        string eventId,
        [FromBody] CreateTopicRequestDto body)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(ApiResponse<ForumTopicDto>.ErrorResponse("UNAUTHORIZED", "Not authenticated"));

        var name = User.FindFirstValue(ClaimTypes.Name) ?? userId;
        var topic = await _forum.CreateEventDiscussionTopicAsync(
            eventId,
            body.Title,
            body.Content,
            userId,
            name,
            body.NoviceVisible,
            body.NoviceCanReply);
        return Ok(ApiResponse<ForumTopicDto>.SuccessResponse(topic));
    }

    [HttpPut("forum-topics/{topicId}")]
    public async Task<ActionResult<ApiResponse<ForumTopicDto>>> UpdateForumTopic(
        string topicId,
        [FromBody] UpdateTopicRequestDto body)
    {
        var t = await _forum.UpdateTopicAsync(topicId, body);
        if (t is null)
            return NotFound(ApiResponse<ForumTopicDto>.ErrorResponse("NOT_FOUND", "Topic not found"));
        return Ok(ApiResponse<ForumTopicDto>.SuccessResponse(t));
    }

    [HttpDelete("forum-topics/{topicId}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteForumTopic(string topicId)
    {
        var ok = await _forum.DeleteTopicAsync(topicId);
        if (!ok)
            return NotFound(ApiResponse<bool>.ErrorResponse("NOT_FOUND", "Topic not found"));
        return Ok(ApiResponse<bool>.SuccessResponse(true));
    }

    /// <summary>Issue or rotate the shared invite code for an event (plaintext returned once).</summary>
    [HttpPost("events/{eventId}/invites")]
    public async Task<ActionResult<ApiResponse<CreateEventInviteResponseDto>>> CreateEventInvite(
        string eventId,
        [FromBody] CreateEventInviteRequestDto request)
    {
        var (plain, exp) = await _eventInvites.CreateOrRotateInviteAsync(eventId, request.ExpiresAtUtc);
        return Ok(ApiResponse<CreateEventInviteResponseDto>.SuccessResponse(
            new CreateEventInviteResponseDto(plain, exp)));
    }

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig()
    {
        var cfg = await _appConfig.GetConfigAsync();
        var dto = new AppConfigDto(
            RankThresholds: new()
            {
                ["active_replies"] = cfg.Ranks.ActiveReplies.ToString(),
                ["active_likes"] = cfg.Ranks.ActiveLikes.ToString(),
                ["active_events"] = cfg.Ranks.ActiveEvents.ToString(),
                ["friend_replies"] = cfg.Ranks.FriendReplies.ToString(),
                ["friend_likes"] = cfg.Ranks.FriendLikes.ToString(),
                ["friend_events"] = cfg.Ranks.FriendEvents.ToString(),
                ["crew_replies"] = cfg.Ranks.CrewReplies.ToString(),
                ["crew_likes"] = cfg.Ranks.CrewLikes.ToString(),
                ["crew_events"] = cfg.Ranks.CrewEvents.ToString(),
                ["crew_matches"] = cfg.Ranks.CrewMatches.ToString(),
            },
            Permissions: new()
            {
                ["create_topic"] = cfg.Permissions.CreateTopic,
                ["delete_own_reply"] = cfg.Permissions.DeleteOwnReply,
                ["delete_any_reply"] = cfg.Permissions.DeleteAnyReply,
                ["delete_any_topic"] = cfg.Permissions.DeleteAnyTopic,
                ["pin_topic"] = cfg.Permissions.PinTopic,
                ["ban_user"] = cfg.Permissions.BanUser,
                ["assign_role"] = cfg.Permissions.AssignRole,
                ["override_rank"] = cfg.Permissions.OverrideRank,
                ["manage_events"] = cfg.Permissions.ManageEvents,
                ["manage_blog"] = cfg.Permissions.ManageBlog,
                ["manage_store"] = cfg.Permissions.ManageStore,
            },
            Registration: new()
            {
                ["require_event_invite"] = cfg.Registration.RequireEventInvite ? "true" : "false",
            });
        return Ok(ApiResponse<AppConfigDto>.SuccessResponse(dto));
    }
}
