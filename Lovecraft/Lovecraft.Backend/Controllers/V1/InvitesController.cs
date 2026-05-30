using Microsoft.AspNetCore.Mvc;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.Models;
using Lovecraft.Backend.Services;

namespace Lovecraft.Backend.Controllers.V1;

/// <summary>
/// Public lookups against invite codes. Mounted at <c>/api/v1/invites</c>. Unauthenticated
/// — invite codes are themselves a credential and the response only leaks the event id of
/// a code the caller already possesses.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class InvitesController : ControllerBase
{
    private readonly IEventInviteService _inviteService;
    private readonly ILogger<InvitesController> _logger;

    public InvitesController(IEventInviteService inviteService, ILogger<InvitesController> logger)
    {
        _inviteService = inviteService;
        _logger = logger;
    }

    /// <summary>
    /// Resolve an invite code to the event it's tied to. Used by the Telegram Mini App to
    /// route a signed-in user — opening <c>t.me/{bot}?startapp=inv-{code}</c> needs to know
    /// the event id before navigating to <c>/aloevera/events/{eventId}?code={code}</c>.
    /// Returns 404 for unknown, revoked, expired, or campaign (negative event id) codes.
    /// </summary>
    [HttpGet("{code}/event")]
    public async Task<ActionResult<ApiResponse<InviteEventLookupDto>>> LookupEvent(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(ApiResponse<InviteEventLookupDto>.ErrorResponse(
                "INVALID_INVITE_CODE", "Invite code is required"));
        }

        var result = await _inviteService.ValidatePlainCodeAsync(code, ct);
        if (result is null)
        {
            return NotFound(ApiResponse<InviteEventLookupDto>.ErrorResponse(
                "INVITE_NOT_FOUND", "Invite not found, revoked, or expired"));
        }

        // Campaign invites use a negative event id sentinel and don't map to a real event.
        if (result.EventId.StartsWith('-'))
        {
            return NotFound(ApiResponse<InviteEventLookupDto>.ErrorResponse(
                "INVITE_IS_CAMPAIGN", "Invite is a campaign code, not tied to an event"));
        }

        return Ok(ApiResponse<InviteEventLookupDto>.SuccessResponse(new InviteEventLookupDto
        {
            EventId = result.EventId,
        }));
    }
}
