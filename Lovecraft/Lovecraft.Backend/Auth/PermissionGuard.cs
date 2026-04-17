using System.Security.Claims;
using Lovecraft.Backend.Helpers;
using Lovecraft.Backend.Services;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Auth;

public static class PermissionGuard
{
    /// <summary>
    /// Returns true when the caller's effective level (max of computed rank and staff role)
    /// meets or exceeds the required level. `requiredLevel` is a string from AppConfig
    /// such as "novice", "activeMember", "moderator", "admin".
    /// </summary>
    public static async Task<bool> MeetsAsync(
        ClaimsPrincipal user,
        IUserService userService,
        string requiredLevel)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return false;

        var staffRoleClaim = user.FindFirst("staffRole")?.Value ?? "none";
        var staffLevel = EffectiveLevel.Parse(staffRoleClaim);

        var dto = await userService.GetUserByIdAsync(userId);
        var rankLevel = (int)(dto?.Rank ?? UserRank.Novice);

        var required = EffectiveLevel.Parse(requiredLevel);
        return Math.Max(rankLevel, staffLevel) >= required;
    }
}
