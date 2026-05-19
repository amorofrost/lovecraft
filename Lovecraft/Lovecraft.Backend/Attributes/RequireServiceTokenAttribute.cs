using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lovecraft.Backend.Attributes;

/// <summary>
/// Action filter: rejects with 401 if the X-Service-Token request header doesn't match
/// the INTERNAL_SERVICE_TOKEN env var. Used for back-channel calls from Lovecraft.TelegramBot
/// into Lovecraft.Backend (e.g. mute-type endpoint).
/// Returns 503 if INTERNAL_SERVICE_TOKEN is not configured.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireServiceTokenAttribute : Attribute, IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var expected = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_TOKEN");
        if (string.IsNullOrEmpty(expected))
        {
            context.Result = new StatusCodeResult(503);
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue("X-Service-Token", out var provided))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided.ToString());
        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
        {
            context.Result = new UnauthorizedResult();
            return;
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
