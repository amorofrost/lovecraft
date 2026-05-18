using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly IPushSubscriptionService _pushSubscriptionService;
    private readonly INotificationPreferenceService _preferenceService;

    public NotificationsController(
        INotificationService notificationService,
        IPushSubscriptionService pushSubscriptionService,
        INotificationPreferenceService preferenceService)
    {
        _notificationService = notificationService;
        _pushSubscriptionService = pushSubscriptionService;
        _preferenceService = preferenceService;
    }

    private string CurrentUserId =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

    // ── Notification list / mutate ───────────────────────────────────────────

    /// <summary>GET /api/v1/notifications?cursor=&amp;limit=20 — paginated list.</summary>
    [HttpGet("notifications")]
    public async Task<ActionResult<ApiResponse<NotificationListResponseDto>>> GetNotifications(
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 20)
    {
        if (limit < 1 || limit > 100)
            return BadRequest(ApiResponse<NotificationListResponseDto>.ErrorResponse(
                "INVALID_LIMIT", "limit must be between 1 and 100"));

        var items = await _notificationService.ListAsync(CurrentUserId, limit, cursor);
        var result = new NotificationListResponseDto
        {
            Items = items,
            NextCursor = items.Count == limit ? items.Last().Id : null,
        };
        return Ok(ApiResponse<NotificationListResponseDto>.SuccessResponse(result));
    }

    /// <summary>GET /api/v1/notifications/unread-count — { count: int }.</summary>
    [HttpGet("notifications/unread-count")]
    public async Task<ActionResult<ApiResponse<UnreadCountResponseDto>>> GetUnreadCount()
    {
        var count = await _notificationService.UnreadCountAsync(CurrentUserId);
        return Ok(ApiResponse<UnreadCountResponseDto>.SuccessResponse(
            new UnreadCountResponseDto { Count = count }));
    }

    /// <summary>POST /api/v1/notifications/{id}/read — mark one notification read.</summary>
    [HttpPost("notifications/{id}/read")]
    public async Task<ActionResult<ApiResponse<object>>> MarkRead(string id)
    {
        var found = await _notificationService.MarkReadAsync(CurrentUserId, id);
        if (!found)
            return NotFound(ApiResponse<object>.ErrorResponse("NOT_FOUND", "Notification not found"));
        return Ok(ApiResponse<object>.SuccessResponse(new { }));
    }

    /// <summary>POST /api/v1/notifications/mark-all-read — bulk mark all as read.</summary>
    [HttpPost("notifications/mark-all-read")]
    public async Task<ActionResult<ApiResponse<object>>> MarkAllRead()
    {
        var updated = await _notificationService.MarkAllReadAsync(CurrentUserId);
        return Ok(ApiResponse<object>.SuccessResponse(new { updated }));
    }

    /// <summary>DELETE /api/v1/notifications/{id} — dismiss one notification.</summary>
    [HttpDelete("notifications/{id}")]
    public async Task<ActionResult<ApiResponse<object>>> DismissNotification(string id)
    {
        var found = await _notificationService.DismissAsync(CurrentUserId, id);
        if (!found)
            return NotFound(ApiResponse<object>.ErrorResponse("NOT_FOUND", "Notification not found"));
        return Ok(ApiResponse<object>.SuccessResponse(new { }));
    }

    // ── Web Push subscriptions ───────────────────────────────────────────────

    /// <summary>POST /api/v1/push/subscribe — register a Web Push subscription.</summary>
    [HttpPost("push/subscribe")]
    public async Task<ActionResult<ApiResponse<WebPushSubscriptionDto>>> Subscribe(
        [FromBody] WebPushSubscriptionRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint))
            return BadRequest(ApiResponse<WebPushSubscriptionDto>.ErrorResponse(
                "ENDPOINT_REQUIRED", "endpoint is required"));

        var dto = await _pushSubscriptionService.SubscribeAsync(CurrentUserId, request);
        return Ok(ApiResponse<WebPushSubscriptionDto>.SuccessResponse(dto));
    }

    /// <summary>DELETE /api/v1/push/subscribe/{deviceId} — unsubscribe one device.</summary>
    [HttpDelete("push/subscribe/{deviceId}")]
    public async Task<ActionResult<ApiResponse<object>>> Unsubscribe(string deviceId)
    {
        var found = await _pushSubscriptionService.UnsubscribeAsync(CurrentUserId, deviceId);
        if (!found)
            return NotFound(ApiResponse<object>.ErrorResponse("NOT_FOUND", "Subscription not found"));
        return Ok(ApiResponse<object>.SuccessResponse(new { }));
    }

    // ── Notification preferences ─────────────────────────────────────────────

    /// <summary>GET /api/v1/notifications/preferences — returns current prefs (or defaults).</summary>
    [HttpGet("notifications/preferences")]
    public async Task<ActionResult<ApiResponse<NotificationPreferencesDto>>> GetPrefs()
    {
        var prefs = await _preferenceService.GetPreferencesAsync(CurrentUserId);
        return Ok(ApiResponse<NotificationPreferencesDto>.SuccessResponse(prefs));
    }

    /// <summary>PUT /api/v1/notifications/preferences — replace prefs with validation.</summary>
    [HttpPut("notifications/preferences")]
    public async Task<ActionResult<ApiResponse<NotificationPreferencesDto>>> UpdatePrefs(
        [FromBody] NotificationPreferencesDto prefs)
    {
        var error = ValidateAndNormalize(prefs);
        if (error is not null)
            return BadRequest(ApiResponse<NotificationPreferencesDto>.ErrorResponse("INVALID_PREFERENCES", error));
        var saved = await _preferenceService.UpdatePreferencesAsync(CurrentUserId, prefs);
        return Ok(ApiResponse<NotificationPreferencesDto>.SuccessResponse(saved));
    }

    private static string? ValidateAndNormalize(NotificationPreferencesDto prefs)
    {
        if (prefs.DailyDigestHourUtc is < 0 or > 23)
            return "dailyDigestHourUtc must be 0-23";
        if (prefs.MutedUntilUtc.HasValue && prefs.MutedUntilUtc.Value <= DateTime.UtcNow)
            return "mutedUntilUtc must be in the future";

        foreach (var typeName in Enum.GetNames<NotificationType>())
        {
            var key = char.ToLowerInvariant(typeName[0]) + typeName[1..];
            if (!prefs.Matrix.TryGetValue(key, out var row))
            {
                row = new Dictionary<string, bool>();
                prefs.Matrix[key] = row;
            }
            row["inApp"]    = true;                              // forced
            if (!row.ContainsKey("telegram")) row["telegram"] = false;
            if (!row.ContainsKey("webPush"))  row["webPush"]  = false;
            if (!row.ContainsKey("email"))    row["email"]    = false;
        }

        prefs.Frequency["inApp"]   = NotificationFrequency.Immediate;
        prefs.Frequency["webPush"] = NotificationFrequency.Immediate;
        if (!prefs.Frequency.ContainsKey("telegram"))
            prefs.Frequency["telegram"] = NotificationFrequency.Immediate;
        if (!prefs.Frequency.ContainsKey("email"))
            prefs.Frequency["email"] = NotificationFrequency.Daily;

        return null;
    }
}
