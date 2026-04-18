using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Admin;
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

    public AdminController(IAppConfigService appConfig)
    {
        _appConfig = appConfig;
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
