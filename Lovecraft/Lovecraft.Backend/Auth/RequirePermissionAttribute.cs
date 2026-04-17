using Lovecraft.Backend.Services;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Lovecraft.Backend.Auth;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequirePermissionAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => false;
    private readonly string _permissionKey;

    public RequirePermissionAttribute(string permissionKey)
    {
        _permissionKey = permissionKey;
    }

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        new Impl(
            serviceProvider.GetRequiredService<IAppConfigService>(),
            serviceProvider.GetRequiredService<IUserService>(),
            _permissionKey);

    private class Impl : IAsyncAuthorizationFilter
    {
        private readonly IAppConfigService _config;
        private readonly IUserService _users;
        private readonly string _key;

        public Impl(IAppConfigService config, IUserService users, string key)
        {
            _config = config;
            _users = users;
            _key = key;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var cfg = await _config.GetConfigAsync();
            var required = LookupRequiredLevel(cfg.Permissions, _key);
            var ok = await PermissionGuard.MeetsAsync(context.HttpContext.User, _users, required);
            if (!ok)
            {
                context.Result = new ObjectResult(
                    ApiResponse<object>.ErrorResponse(
                        AuthorizationErrors.InsufficientRank,
                        AuthorizationErrors.InsufficientRankMessage))
                { StatusCode = StatusCodes.Status403Forbidden };
            }
        }

        private static string LookupRequiredLevel(PermissionConfig p, string key) => key switch
        {
            "create_topic" => p.CreateTopic,
            "delete_own_reply" => p.DeleteOwnReply,
            "delete_any_reply" => p.DeleteAnyReply,
            "delete_any_topic" => p.DeleteAnyTopic,
            "pin_topic" => p.PinTopic,
            "ban_user" => p.BanUser,
            "assign_role" => p.AssignRole,
            "override_rank" => p.OverrideRank,
            "manage_events" => p.ManageEvents,
            "manage_blog" => p.ManageBlog,
            "manage_store" => p.ManageStore,
            _ => "admin",
        };
    }
}
