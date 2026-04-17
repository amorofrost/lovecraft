using Lovecraft.Backend.Helpers;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lovecraft.Backend.Auth;

/// <summary>
/// Requires the caller's staffRole claim meet at least the given minimum.
/// Valid values: "moderator" | "admin".
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequireStaffRoleAttribute : Attribute, IAuthorizationFilter
{
    private readonly int _requiredLevel;
    private readonly string _errorCode;
    private readonly string _errorMessage;

    public RequireStaffRoleAttribute(string minimumRole)
    {
        _requiredLevel = EffectiveLevel.Parse(minimumRole);
        if (_requiredLevel >= EffectiveLevel.Admin)
        {
            _errorCode = AuthorizationErrors.AdminRequired;
            _errorMessage = AuthorizationErrors.AdminRequiredMessage;
        }
        else
        {
            _errorCode = AuthorizationErrors.ModeratorRequired;
            _errorMessage = AuthorizationErrors.ModeratorRequiredMessage;
        }
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var staffRole = context.HttpContext.User.FindFirst("staffRole")?.Value ?? "none";
        var actualLevel = EffectiveLevel.Parse(staffRole);
        if (actualLevel < _requiredLevel)
        {
            context.Result = new ObjectResult(
                ApiResponse<object>.ErrorResponse(_errorCode, _errorMessage))
            { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
