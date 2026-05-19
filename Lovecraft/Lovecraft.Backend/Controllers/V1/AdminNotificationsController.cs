using System.Security.Claims;
using System.Text.Json;
using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lovecraft.Backend.Controllers.V1;

/// <summary>
/// Admin-only endpoints for issuing and inspecting community broadcasts. The POST endpoint
/// computes the recipient set synchronously (so the caller gets an accurate
/// <c>estimatedRecipients</c> count immediately) and then fans the producer calls out on
/// a background <see cref="Task.Run"/> — the HTTP response returns as soon as the row is
/// written, never blocking on hundreds of individual <c>ProduceAsync</c> invocations.
/// </summary>
[ApiController]
[Route("api/v1/admin/notifications")]
[Authorize]
[RequireStaffRole("admin")]
public class AdminNotificationsController : ControllerBase
{
    private readonly IBroadcastService _broadcasts;
    private readonly IBroadcastAudienceResolver _resolver;
    private readonly INotificationProducer _producer;
    private readonly ILogger<AdminNotificationsController> _logger;

    public AdminNotificationsController(
        IBroadcastService broadcasts,
        IBroadcastAudienceResolver resolver,
        INotificationProducer producer,
        ILogger<AdminNotificationsController> logger)
    {
        _broadcasts = broadcasts;
        _resolver = resolver;
        _producer = producer;
        _logger = logger;
    }

    [HttpPost("broadcast")]
    public async Task<ActionResult<ApiResponse<BroadcastDto>>> CreateBroadcast(
        [FromBody] CreateBroadcastRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<BroadcastDto>.ErrorResponse(
                "VALIDATION_ERROR", "Title, body, and audience are required"));

        if (request.Audience is null || string.IsNullOrWhiteSpace(request.Audience.Type))
            return BadRequest(ApiResponse<BroadcastDto>.ErrorResponse(
                "VALIDATION_ERROR", "Audience type is required"));

        var issuedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(issuedByUserId))
            return Unauthorized(ApiResponse<BroadcastDto>.ErrorResponse(
                "UNAUTHORIZED", "Not authenticated"));

        var recipients = await _resolver.ResolveAsync(request.Audience, ct);

        var bc = await _broadcasts.CreateAsync(request, issuedByUserId);
        await _broadcasts.SetEstimatedRecipientsAsync(bc.Id, recipients.Count);
        bc.EstimatedRecipients = recipients.Count;

        // Capture locals so the background task does not touch HttpContext-scoped state.
        var producer = _producer;
        var broadcastSvc = _broadcasts;
        var logger = _logger;
        var broadcastId = bc.Id;
        var sourceEventId = $"broadcast-{bc.Id}";
        var payload = JsonSerializer.Serialize(new
        {
            title = bc.Title,
            body = bc.Body,
            link = bc.Link,
        });
        var actorId = issuedByUserId;
        var recipientList = recipients;

        // Fan out asynchronously — DO NOT await. The Phase E review caught the inline-await
        // anti-pattern; broadcasting to a large audience must not block the request thread.
        _ = Task.Run(async () =>
        {
            var dispatched = 0;
            foreach (var recipientId in recipientList)
            {
                try
                {
                    await producer.ProduceAsync(
                        recipientId,
                        NotificationType.CommunityBroadcast,
                        actorId: actorId,
                        payloadJson: payload,
                        sourceEventId: sourceEventId,
                        presenceGroup: null);
                    dispatched++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Broadcast {BroadcastId} producer failed for {RecipientId}",
                        broadcastId, recipientId);
                }
            }

            try
            {
                await broadcastSvc.SetCompletedAsync(broadcastId, dispatched, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Broadcast {BroadcastId} completion update failed", broadcastId);
            }
        });

        return Ok(ApiResponse<BroadcastDto>.SuccessResponse(bc));
    }

    [HttpGet("broadcasts")]
    public async Task<ActionResult<ApiResponse<List<BroadcastDto>>>> ListBroadcasts([FromQuery] int limit = 50)
    {
        if (limit < 1 || limit > 200) limit = 50;
        var list = await _broadcasts.ListAsync(limit);
        return Ok(ApiResponse<List<BroadcastDto>>.SuccessResponse(list));
    }

    [HttpGet("broadcasts/{broadcastId}")]
    public async Task<ActionResult<ApiResponse<BroadcastDto>>> GetBroadcast(string broadcastId)
    {
        var bc = await _broadcasts.GetByIdAsync(broadcastId);
        if (bc is null)
            return NotFound(ApiResponse<BroadcastDto>.ErrorResponse("NOT_FOUND", "Broadcast not found"));
        return Ok(ApiResponse<BroadcastDto>.SuccessResponse(bc));
    }
}
