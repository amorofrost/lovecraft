using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.Models;
using Lovecraft.Backend.Services;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize] // Require authentication
public class EventsController : ControllerBase
{
    private readonly IEventService _eventService;
    private readonly ILogger<EventsController> _logger;

    public EventsController(IEventService eventService, ILogger<EventsController> logger)
    {
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get all events
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<EventDto>>>> GetEvents()
    {
        try
        {
            var events = await _eventService.GetEventsAsync();
            return Ok(ApiResponse<List<EventDto>>.SuccessResponse(events));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting events");
            return StatusCode(500, ApiResponse<List<EventDto>>.ErrorResponse("INTERNAL_ERROR", "Failed to get events"));
        }
    }

    /// <summary>
    /// Get event by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<EventDto>>> GetEvent(string id)
    {
        try
        {
            var eventDto = await _eventService.GetEventByIdAsync(id);
            if (eventDto == null)
            {
                return NotFound(ApiResponse<EventDto>.ErrorResponse("NOT_FOUND", "Event not found"));
            }
            return Ok(ApiResponse<EventDto>.SuccessResponse(eventDto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting event {EventId}", id);
            return StatusCode(500, ApiResponse<EventDto>.ErrorResponse("INTERNAL_ERROR", "Failed to get event"));
        }
    }

    /// <summary>
    /// Register for an event
    /// </summary>
    [HttpPost("{id}/register")]
    public async Task<ActionResult<ApiResponse<bool>>> RegisterForEvent(string id)
    {
        try
        {
            // For now, use hardcoded current user
            const string currentUserId = "current-user";
            var result = await _eventService.RegisterForEventAsync(currentUserId, id);
            
            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("REGISTRATION_FAILED", "Failed to register for event"));
            }
            
            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering for event {EventId}", id);
            return StatusCode(500, ApiResponse<bool>.ErrorResponse("INTERNAL_ERROR", "Failed to register for event"));
        }
    }

    /// <summary>
    /// Unregister from an event
    /// </summary>
    [HttpDelete("{id}/register")]
    public async Task<ActionResult<ApiResponse<bool>>> UnregisterFromEvent(string id)
    {
        try
        {
            const string currentUserId = "current-user";
            var result = await _eventService.UnregisterFromEventAsync(currentUserId, id);
            
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
