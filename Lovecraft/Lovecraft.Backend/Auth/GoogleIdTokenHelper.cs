using Google.Apis.Auth;
using Lovecraft.Common.DTOs.Auth;
using Microsoft.Extensions.Logging;

namespace Lovecraft.Backend.Auth;

public static class GoogleIdTokenHelper
{
    public static async Task<GoogleUserInfoDto?> ValidateAndExtractAsync(
        string idToken,
        string clientId,
        ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(clientId))
            return null;

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [clientId]
            };
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken, settings);

            if (string.IsNullOrEmpty(payload.Subject))
                return null;

            if (string.IsNullOrEmpty(payload.Email))
            {
                logger?.LogWarning("Google ID token missing email claim (sub: {Sub})", payload.Subject);
                return null;
            }

            var name = !string.IsNullOrEmpty(payload.Name) ? payload.Name! : (payload.Email ?? "User");

            return new GoogleUserInfoDto
            {
                Sub = payload.Subject,
                Email = payload.Email!,
                EmailVerified = payload.EmailVerified,
                Name = name,
                PictureUrl = payload.Picture
            };
        }
        catch (InvalidJwtException ex)
        {
            // Token is structurally invalid, expired, wrong audience, or signature mismatch.
            logger?.LogWarning(ex, "Google ID token validation failed (invalid token)");
            return null;
        }
        // Other exceptions (HttpRequestException, TaskCanceledException, etc.) propagate so
        // the controller can return 500/503 instead of a misleading GOOGLE_TOKEN_INVALID 400.
    }
}
