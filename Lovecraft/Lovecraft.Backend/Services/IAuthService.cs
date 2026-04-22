using Lovecraft.Common.DTOs.Auth;

namespace Lovecraft.Backend.Services;

public interface IAuthService
{
    Task<AuthResponseDto?> RegisterAsync(RegisterRequestDto request);
    Task<AuthResponseDto?> LoginAsync(LoginRequestDto request);

    /// <summary>
    /// Telegram Login Widget: verify signature. Returns <c>signedIn</c> with JWT pair for known
    /// Telegram ids, or <c>pending</c> with a short-lived ticket for unknown ids (frontend then
    /// routes the user to /welcome/telegram to create/link an account). No user row is written
    /// during the pending branch.
    /// </summary>
    Task<TelegramLoginResultDto?> TelegramLoginAsync(TelegramLoginRequestDto request);

    /// <summary>Create a new account from a verified Telegram ticket + profile fields + optional invite.</summary>
    Task<AuthResponseDto?> TelegramRegisterAsync(TelegramRegisterRequestDto request);

    /// <summary>Link a verified Telegram ticket to an existing email/password account in one call.</summary>
    Task<AuthResponseDto?> TelegramLinkLoginAsync(TelegramLinkLoginRequestDto request);

    /// <summary>Authenticated: link a verified Telegram ticket to <paramref name="userId"/>.</summary>
    Task<AuthResponseDto?> TelegramLinkAsync(string userId, string ticket);

    /// <summary>
    /// Authenticated (Telegram-only account): request to add an email+password. Sends a verification
    /// email. The email + password are only applied to the user row (and <c>local</c> added to
    /// AuthMethods) when the verification link is clicked.
    /// </summary>
    Task<AttachEmailResult> RequestEmailAttachAsync(string userId, string email, string password);

    /// <summary>
    /// Telegram Mini App: validate <c>initData</c>, then either sign in (known tg id) or return
    /// <c>needsRegistration</c> with the verified Telegram identity so the Mini App can render an
    /// inline profile wizard / link-account prompt. No user row is written in the <c>needsRegistration</c> branch.
    /// </summary>
    Task<TelegramMiniAppLoginResultDto?> MiniAppLoginAsync(TelegramMiniAppLoginRequestDto request);

    /// <summary>Mini App: create a new account from verified initData + profile fields + optional invite.</summary>
    Task<AuthResponseDto?> MiniAppRegisterAsync(TelegramMiniAppRegisterRequestDto request);

    /// <summary>Mini App: link verified initData to an existing email/password account in one call.</summary>
    Task<AuthResponseDto?> MiniAppLinkLoginAsync(TelegramMiniAppLinkLoginRequestDto request);

    Task<AuthResponseDto?> RefreshTokenAsync(string refreshToken);
    Task<bool> VerifyEmailAsync(string token);
    Task<bool> ForgotPasswordAsync(string email);
    Task<bool> ResetPasswordAsync(string token, string newPassword);
    Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword);
    Task<UserInfo?> GetCurrentUserAsync(string userId);
    Task<List<AuthMethodDto>> GetAuthMethodsAsync(string userId);
    Task RevokeRefreshTokenAsync(string refreshToken);
    Task RevokeAllUserTokensAsync(string userId);
}

public enum AttachEmailResult
{
    Ok,
    UserNotFound,
    EmailAlreadyTaken,
    AlreadyHasLocal,
    ReservedDomain
}
