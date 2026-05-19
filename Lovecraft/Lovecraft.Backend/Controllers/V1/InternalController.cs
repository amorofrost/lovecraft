using Lovecraft.Backend.Attributes;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/internal")]
[RequireServiceToken]
public class InternalController : ControllerBase
{
    private readonly IUserService _users;
    private readonly INotificationPreferenceService _prefs;

    public InternalController(IUserService users, INotificationPreferenceService prefs)
    {
        _users = users;
        _prefs = prefs;
    }

    [HttpPost("notifications/mute-type")]
    public async Task<IActionResult> MuteType([FromBody] InternalMuteTypeRequestDto request)
    {
        if (string.IsNullOrEmpty(request.TelegramUserId) || string.IsNullOrEmpty(request.Type))
            return BadRequest();

        var userId = await _users.GetUserIdByTelegramIdAsync(request.TelegramUserId);
        if (userId is null)
            return NotFound();

        await _prefs.SetChannelDisabledForTypeAsync(userId, request.Type, "telegram");
        return NoContent();
    }
}
