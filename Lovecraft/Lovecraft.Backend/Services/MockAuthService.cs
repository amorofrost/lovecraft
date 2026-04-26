using System.Security.Cryptography;
using Lovecraft.Common.DTOs.Auth;
using Lovecraft.Common.Enums;
using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Configuration;
using Lovecraft.Backend.MockData;
using Microsoft.Extensions.Options;

namespace Lovecraft.Backend.Services;

public class MockAuthService : IAuthService
{
    private const string TelegramSyntheticEmailDomain = "@telegram.local";

    private readonly IJwtService _jwtService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<MockAuthService> _logger;
    private readonly IEmailService _emailService;
    private readonly IAppConfigService _appConfig;
    private readonly IEventInviteService _eventInvites;
    private readonly IEventService _events;
    private readonly TelegramAuthOptions _telegramOptions;
    private readonly GoogleAuthOptions _googleOptions;

    // Mock in-memory storage
    private static readonly Dictionary<string, MockUser> _users = new();
    private static readonly Dictionary<string, string> _refreshTokens = new();
    private static readonly Dictionary<string, string> _verificationTokens = new();
    private static readonly Dictionary<string, PasswordResetToken> _resetTokens = new();
    /// <summary>Telegram user id string → user email key in <see cref="_users"/>.</summary>
    private static readonly Dictionary<string, string> _telegramToUserKey = new();
    private static readonly Dictionary<string, string> _googleSubToUserKey = new();
    /// <summary>ATTACH tokens: pending email+password swap for Telegram-only accounts.</summary>
    private static readonly Dictionary<string, AttachPending> _attachTokens = new();

    public MockAuthService(
        IJwtService jwtService,
        IPasswordHasher passwordHasher,
        ILogger<MockAuthService> logger,
        IEmailService emailService,
        IAppConfigService appConfig,
        IEventInviteService eventInvites,
        IEventService events,
        IOptions<TelegramAuthOptions> telegramOptions,
        IOptions<GoogleAuthOptions> googleOptions)
    {
        _jwtService = jwtService;
        _passwordHasher = passwordHasher;
        _logger = logger;
        _emailService = emailService;
        _appConfig = appConfig;
        _eventInvites = eventInvites;
        _events = events;
        _telegramOptions = telegramOptions.Value;
        _googleOptions = googleOptions.Value;

        // Seed with test user
        SeedTestUsers();
    }

    private void SeedTestUsers()
    {
        if (_users.Count == 0)
        {
            var testUser = new MockUser
            {
                Id = "test-user-001",
                Email = "test@example.com",
                Name = "Test User",
                PasswordHash = _passwordHasher.HashPassword("Test123!@#"),
                EmailVerified = true,
                AuthMethods = new List<string> { "local" },
                CreatedAt = DateTime.UtcNow
            };
            _users[testUser.Email.ToLower()] = testUser;
        }
    }

    public async Task<AuthResponseDto?> RegisterAsync(RegisterRequestDto request)
    {
        await Task.Delay(100); // Simulate async operation

        if (request.Email.EndsWith(TelegramSyntheticEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Registration blocked: reserved Telegram synthetic domain {Email}", request.Email);
            return null;
        }

        var sourceEventId = await ResolveInviteSourceAsync(request.InviteCode);

        // Validate email doesn't exist
        if (_users.ContainsKey(request.Email.ToLower()))
        {
            _logger.LogWarning("Registration failed: Email already exists {Email}", request.Email);
            return null;
        }

        // Create new user
        var userId = Guid.NewGuid().ToString();
        var user = new MockUser
        {
            Id = userId,
            Email = request.Email,
            Name = request.Name,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            EmailVerified = false, // Must verify email
            AuthMethods = new List<string> { "local" },
            CreatedAt = DateTime.UtcNow,
            Age = request.Age,
            Location = request.Location,
            Gender = NormalizeGender(request.Gender),
            Bio = request.Bio
        };

        _users[user.Email.ToLower()] = user;

        if (sourceEventId is not null && !EventInviteHelpers.IsCampaignEventId(sourceEventId))
            await _events.RegisterForEventAsync(userId, sourceEventId);

        if (!string.IsNullOrWhiteSpace(request.InviteCode))
            await _eventInvites.IncrementRegistrationCountAsync(request.InviteCode);

        // Generate email verification token
        var verificationToken = Guid.NewGuid().ToString();
        _verificationTokens[verificationToken] = userId;

        _logger.LogInformation("User registered: {UserId}, Email: {Email}", userId, request.Email);

        try
        {
            await _emailService.SendVerificationEmailAsync(user.Email, user.Name, verificationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send verification email to {Email}; token remains valid", user.Email);
        }

        // Generate tokens (but user can't use them until email verified)
        var accessToken = _jwtService.GenerateAccessToken(userId, user.Email, user.Name, "none");
        var refreshToken = _jwtService.GenerateRefreshToken();
        _refreshTokens[refreshToken] = userId;

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = new UserInfo
            {
                Id = userId,
                Email = user.Email,
                Name = user.Name,
                EmailVerified = false,
                AuthMethods = user.AuthMethods,
                ProfileImage = user.ProfileImage,
            },
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };
    }

    public async Task<TelegramLoginResultDto?> TelegramLoginAsync(TelegramLoginRequestDto request)
    {
        await Task.Delay(50);

        if (string.IsNullOrWhiteSpace(_telegramOptions.BotToken))
        {
            _logger.LogWarning("Telegram login: BotToken not configured");
            return null;
        }

        if (!TelegramLoginVerifier.Verify(_telegramOptions.BotToken, request))
        {
            _logger.LogWarning("Telegram login: invalid signature for id {Id}", request.Id);
            return null;
        }

        var tgKey = request.Id.ToString();

        if (_telegramToUserKey.TryGetValue(tgKey, out var emailKey) &&
            _users.TryGetValue(emailKey, out var existing))
        {
            var auth = await IssueJwtPairAsync(existing);
            return new TelegramLoginResultDto { Status = "signedIn", Auth = auth };
        }

        var tgInfo = new TelegramUserInfoDto
        {
            Id = request.Id,
            FirstName = request.FirstName ?? string.Empty,
            LastName = request.LastName,
            Username = request.Username,
            PhotoUrl = request.PhotoUrl,
        };
        var ticket = _jwtService.GenerateTelegramPendingTicket(tgInfo);
        _logger.LogInformation("Mock Telegram login: pending ticket for tg {TgId}", tgKey);
        return new TelegramLoginResultDto { Status = "pending", Ticket = ticket, Telegram = tgInfo };
    }

    public async Task<AuthResponseDto?> TelegramRegisterAsync(TelegramRegisterRequestDto request)
    {
        await Task.Delay(50);

        var tgInfo = _jwtService.ValidateTelegramPendingTicket(request.Ticket);
        if (tgInfo is null)
        {
            _logger.LogWarning("Telegram register: invalid ticket");
            return null;
        }

        var tgKey = tgInfo.Id.ToString();
        if (_telegramToUserKey.ContainsKey(tgKey))
        {
            _logger.LogWarning("Telegram register: tg {TgId} already linked", tgKey);
            return null;
        }

        var sourceEventId = await ResolveInviteSourceAsync(request.InviteCode);

        var syntheticEmail = $"telegram_{tgInfo.Id}{TelegramSyntheticEmailDomain}";
        var key = syntheticEmail.ToLowerInvariant();
        if (_users.ContainsKey(key))
        {
            syntheticEmail = $"telegram_{tgInfo.Id}_{Guid.NewGuid():N}{TelegramSyntheticEmailDomain}";
            key = syntheticEmail.ToLowerInvariant();
        }

        var userId = Guid.NewGuid().ToString();
        var displayName = string.IsNullOrWhiteSpace(request.Name)
            ? (string.IsNullOrWhiteSpace(tgInfo.LastName)
                ? tgInfo.FirstName.Trim()
                : $"{tgInfo.FirstName.Trim()} {tgInfo.LastName!.Trim()}")
            : request.Name.Trim();

        var user = new MockUser
        {
            Id = userId,
            Email = syntheticEmail,
            Name = displayName,
            PasswordHash = _passwordHasher.HashPassword(Guid.NewGuid().ToString("N")),
            EmailVerified = true,
            AuthMethods = new List<string> { "telegram" },
            CreatedAt = DateTime.UtcNow,
            Age = request.Age > 0 ? request.Age : 18,
            Location = string.IsNullOrWhiteSpace(request.Location) ? "Telegram" : request.Location,
            Gender = NormalizeGender(request.Gender),
            Bio = request.Bio ?? string.Empty,
            TelegramUserId = tgKey,
        };

        _users[key] = user;
        _telegramToUserKey[tgKey] = key;

        if (sourceEventId is not null && !EventInviteHelpers.IsCampaignEventId(sourceEventId))
            await _events.RegisterForEventAsync(userId, sourceEventId);
        if (!string.IsNullOrWhiteSpace(request.InviteCode))
            await _eventInvites.IncrementRegistrationCountAsync(request.InviteCode);

        _logger.LogInformation("Mock Telegram user registered: {UserId}, tg {TgId}", userId, tgKey);
        return await IssueJwtPairAsync(user);
    }

    public async Task<AuthResponseDto?> TelegramLinkLoginAsync(TelegramLinkLoginRequestDto request)
    {
        await Task.Delay(50);

        var tgInfo = _jwtService.ValidateTelegramPendingTicket(request.Ticket);
        if (tgInfo is null) return null;

        if (!_users.TryGetValue(request.Email.ToLower(), out var user)) return null;
        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash)) return null;
        if (!user.EmailVerified) return null;

        if (!AttachTelegramToUser(user, tgInfo)) return null;

        return await IssueJwtPairAsync(user);
    }

    public async Task<AuthResponseDto?> TelegramLinkAsync(string userId, string ticket)
    {
        await Task.Delay(50);

        var tgInfo = _jwtService.ValidateTelegramPendingTicket(ticket);
        if (tgInfo is null) return null;

        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null) return null;

        if (!AttachTelegramToUser(user, tgInfo)) return null;

        return await IssueJwtPairAsync(user);
    }

    public async Task<TelegramMiniAppLoginResultDto?> MiniAppLoginAsync(TelegramMiniAppLoginRequestDto request)
    {
        await Task.Delay(50);

        if (string.IsNullOrWhiteSpace(_telegramOptions.BotToken))
        {
            _logger.LogWarning("Mini app login: BotToken not configured");
            return null;
        }

        var tgInfo = TelegramInitDataValidator.Validate(_telegramOptions.BotToken, request.InitData);
        if (tgInfo is null)
        {
            _logger.LogWarning("Mini app login: invalid initData");
            return null;
        }

        var tgKey = tgInfo.Id.ToString();
        if (_telegramToUserKey.TryGetValue(tgKey, out var emailKey) &&
            _users.TryGetValue(emailKey, out var existing))
        {
            var auth = await IssueJwtPairAsync(existing);
            return new TelegramMiniAppLoginResultDto { Status = "signedIn", Auth = auth, Telegram = tgInfo };
        }

        _logger.LogInformation("Mock Mini app login: needsRegistration for tg {TgId}", tgKey);
        return new TelegramMiniAppLoginResultDto { Status = "needsRegistration", Telegram = tgInfo };
    }

    public async Task<AuthResponseDto?> MiniAppRegisterAsync(TelegramMiniAppRegisterRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(_telegramOptions.BotToken)) return null;
        var tgInfo = TelegramInitDataValidator.Validate(_telegramOptions.BotToken, request.InitData);
        if (tgInfo is null) return null;

        var ticket = _jwtService.GenerateTelegramPendingTicket(tgInfo);
        return await TelegramRegisterAsync(new TelegramRegisterRequestDto
        {
            Ticket = ticket,
            Name = request.Name,
            Age = request.Age,
            Location = request.Location,
            Gender = request.Gender,
            Bio = request.Bio,
            InviteCode = request.InviteCode,
        });
    }

    public async Task<AuthResponseDto?> MiniAppLinkLoginAsync(TelegramMiniAppLinkLoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(_telegramOptions.BotToken)) return null;
        var tgInfo = TelegramInitDataValidator.Validate(_telegramOptions.BotToken, request.InitData);
        if (tgInfo is null) return null;

        var ticket = _jwtService.GenerateTelegramPendingTicket(tgInfo);
        return await TelegramLinkLoginAsync(new TelegramLinkLoginRequestDto
        {
            Email = request.Email,
            Password = request.Password,
            Ticket = ticket,
        });
    }

    public async Task<GoogleLoginResultDto?> GoogleLoginAsync(string idToken)
    {
        if (string.IsNullOrWhiteSpace(_googleOptions.ClientId))
        {
            _logger.LogWarning("Google login: ClientId not configured");
            return null;
        }

        var google = await GoogleIdTokenHelper.ValidateAndExtractAsync(
            idToken, _googleOptions.ClientId, _logger);
        if (google is null) return null;

        var sub = google.Sub;
        var emailLower = google.Email.ToLower();

        if (_googleSubToUserKey.TryGetValue(sub, out var subEmailKey) &&
            _users.TryGetValue(subEmailKey, out var byIndex))
        {
            return new GoogleLoginResultDto { Status = "signedIn", Auth = await IssueJwtPairAsync(byIndex) };
        }

        if (_users.TryGetValue(emailLower, out var byEmail))
        {
            if (string.IsNullOrEmpty(byEmail.GoogleUserId))
            {
                if (!await MockAttachGoogleAsync(byEmail, google))
                {
                    return new GoogleLoginResultDto
                    {
                        Status = "emailConflict",
                        Message = "Could not link Google to this account."
                    };
                }
                return new GoogleLoginResultDto
                {
                    Status = "signedIn",
                    Auth = await IssueJwtPairAsync(_users[emailLower]!)
                };
            }
            if (string.Equals(byEmail.GoogleUserId, sub, StringComparison.Ordinal))
            {
                if (!_googleSubToUserKey.ContainsKey(sub))
                    _googleSubToUserKey[sub] = emailLower;
                return new GoogleLoginResultDto { Status = "signedIn", Auth = await IssueJwtPairAsync(byEmail) };
            }
            return new GoogleLoginResultDto
            {
                Status = "emailConflict",
                Message = "This email is already associated with a different Google account."
            };
        }

        var ticket = _jwtService.GenerateGooglePendingTicket(google);
        return new GoogleLoginResultDto
        {
            Status = "pending",
            Ticket = ticket,
            Google = google
        };
    }

    public async Task<AuthResponseDto?> GoogleRegisterAsync(GoogleRegisterRequestDto request)
    {
        await Task.Delay(50);
        var gInfo = _jwtService.ValidateGooglePendingTicket(request.Ticket);
        if (gInfo is null) return null;

        if (gInfo.Email.EndsWith(TelegramSyntheticEmailDomain, StringComparison.OrdinalIgnoreCase))
            return null;

        if (_googleSubToUserKey.ContainsKey(gInfo.Sub)) return null;

        var emailKey = gInfo.Email.ToLower();
        if (_users.ContainsKey(emailKey)) return null;

        var sourceEventId = await ResolveInviteSourceAsync(request.InviteCode);

        var userId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var displayName = string.IsNullOrWhiteSpace(request.Name) ? gInfo.Name.Trim() : request.Name.Trim();
        if (string.IsNullOrEmpty(displayName)) displayName = gInfo.Email;

        var user = new MockUser
        {
            Id = userId,
            Email = gInfo.Email,
            Name = displayName,
            PasswordHash = _passwordHasher.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))),
            EmailVerified = gInfo.EmailVerified,
            AuthMethods = new List<string> { "google" },
            CreatedAt = now,
            Age = request.Age > 0 ? request.Age : 18,
            Location = string.IsNullOrWhiteSpace(request.Location) ? "—" : request.Location,
            Gender = NormalizeGender(request.Gender),
            Bio = request.Bio ?? string.Empty,
            GoogleUserId = gInfo.Sub,
        };

        _users[emailKey] = user;
        _googleSubToUserKey[gInfo.Sub] = emailKey;

        if (sourceEventId is not null && !EventInviteHelpers.IsCampaignEventId(sourceEventId))
            await _events.RegisterForEventAsync(userId, sourceEventId);

        if (!string.IsNullOrWhiteSpace(request.InviteCode))
            await _eventInvites.IncrementRegistrationCountAsync(request.InviteCode);

        return await IssueJwtPairAsync(user);
    }

    private async Task<bool> MockAttachGoogleAsync(MockUser user, GoogleUserInfoDto gInfo)
    {
        await Task.Yield();
        if (_googleSubToUserKey.ContainsKey(gInfo.Sub))
        {
            if (_googleSubToUserKey[gInfo.Sub] != user.Email.ToLower()) return false;
            return true;
        }
        _googleSubToUserKey[gInfo.Sub] = user.Email.ToLower();
        if (!user.AuthMethods.Contains("google", StringComparer.OrdinalIgnoreCase)) user.AuthMethods.Add("google");
        user.GoogleUserId = gInfo.Sub;
        return true;
    }

    public async Task<AttachEmailResult> RequestEmailAttachAsync(string userId, string email, string password)
    {
        await Task.Delay(50);

        if (email.EndsWith(TelegramSyntheticEmailDomain, StringComparison.OrdinalIgnoreCase))
            return AttachEmailResult.ReservedDomain;

        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null) return AttachEmailResult.UserNotFound;

        if (user.AuthMethods.Contains("local", StringComparer.OrdinalIgnoreCase))
            return AttachEmailResult.AlreadyHasLocal;

        if (_users.ContainsKey(email.ToLower()))
            return AttachEmailResult.EmailAlreadyTaken;

        // Drop any earlier pending attach for this user.
        var stale = _attachTokens.Where(kv => kv.Value.UserId == userId).Select(kv => kv.Key).ToList();
        foreach (var k in stale) _attachTokens.Remove(k);

        var token = Guid.NewGuid().ToString();
        _attachTokens[token] = new AttachPending
        {
            UserId = userId,
            Email = email,
            PendingPasswordHash = _passwordHasher.HashPassword(password),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        };

        try
        {
            await _emailService.SendVerificationEmailAsync(email, user.Name, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send attach-email verification to {Email}", email);
        }

        return AttachEmailResult.Ok;
    }

    private bool AttachTelegramToUser(MockUser user, TelegramUserInfoDto tgInfo)
    {
        var tgKey = tgInfo.Id.ToString();

        if (_telegramToUserKey.TryGetValue(tgKey, out var ownedBy))
        {
            if (ownedBy != user.Email.ToLower()) return false; // owned by someone else
            return true; // already linked to this user
        }

        if (!user.AuthMethods.Contains("telegram", StringComparer.OrdinalIgnoreCase))
            user.AuthMethods.Add("telegram");
        user.TelegramUserId = tgKey;
        _telegramToUserKey[tgKey] = user.Email.ToLower();
        return true;
    }

    private async Task<AuthResponseDto> IssueJwtPairAsync(MockUser user)
    {
        await Task.Yield();
        var staffRole = MockDataStore.UserStaffRoles.TryGetValue(user.Id, out var role)
            ? role.ToString().ToLowerInvariant()
            : "none";
        var accessToken = _jwtService.GenerateAccessToken(user.Id, user.Email, user.Name, staffRole);
        var refreshToken = _jwtService.GenerateRefreshToken();
        _refreshTokens[refreshToken] = user.Id;
        user.LastLoginAt = DateTime.UtcNow;

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = new UserInfo
            {
                Id = user.Id,
                Email = user.Email,
                Name = user.Name,
                EmailVerified = user.EmailVerified,
                AuthMethods = user.AuthMethods,
                ProfileImage = user.ProfileImage,
            },
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
        };
    }

    private async Task<string?> ResolveInviteSourceAsync(string? inviteCode)
    {
        var cfg = await _appConfig.GetConfigAsync();
        if (!string.IsNullOrWhiteSpace(inviteCode))
        {
            var val = await _eventInvites.ValidatePlainCodeAsync(inviteCode);
            if (val is null) throw new InvalidInviteCodeException();
            return val.EventId;
        }
        if (cfg.Registration.RequireEventInvite) throw new InviteRequiredException();
        return null;
    }

    public async Task<AuthResponseDto?> LoginAsync(LoginRequestDto request)
    {
        await Task.Delay(100); // Simulate async operation

        var identifier = request.Email.ToLower();
        
        if (!_users.TryGetValue(identifier, out var user))
        {
            _logger.LogWarning("Login failed: User not found {Email}", request.Email);
            return null;
        }

        // Verify password
        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed: Invalid password for {Email}", request.Email);
            return null;
        }

        // Check email verification
        if (!user.EmailVerified)
        {
            _logger.LogWarning("Login failed: Email not verified for {Email}", request.Email);
            return null;
        }

        // Generate tokens
        var staffRole = MockDataStore.UserStaffRoles.TryGetValue(user.Id, out var role)
            ? role.ToString().ToLowerInvariant()
            : "none";
        var accessToken = _jwtService.GenerateAccessToken(user.Id, user.Email, user.Name, staffRole);
        var refreshToken = _jwtService.GenerateRefreshToken();
        _refreshTokens[refreshToken] = user.Id;

        user.LastLoginAt = DateTime.UtcNow;

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = new UserInfo
            {
                Id = user.Id,
                Email = user.Email,
                Name = user.Name,
                EmailVerified = user.EmailVerified,
                AuthMethods = user.AuthMethods,
                ProfileImage = user.ProfileImage,
            },
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };
    }

    public async Task<AuthResponseDto?> RefreshTokenAsync(string refreshToken)
    {
        await Task.Delay(50);

        if (!_refreshTokens.TryGetValue(refreshToken, out var userId))
        {
            _logger.LogWarning("Refresh token not found");
            return null;
        }

        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            return null;
        }

        // Revoke old refresh token
        _refreshTokens.Remove(refreshToken);

        // Generate new tokens
        var staffRole = MockDataStore.UserStaffRoles.TryGetValue(user.Id, out var role)
            ? role.ToString().ToLowerInvariant()
            : "none";
        var newAccessToken = _jwtService.GenerateAccessToken(user.Id, user.Email, user.Name, staffRole);
        var newRefreshToken = _jwtService.GenerateRefreshToken();
        _refreshTokens[newRefreshToken] = userId;

        return new AuthResponseDto
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            User = new UserInfo
            {
                Id = user.Id,
                Email = user.Email,
                Name = user.Name,
                EmailVerified = user.EmailVerified,
                AuthMethods = user.AuthMethods,
                ProfileImage = user.ProfileImage,
            },
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };
    }

    public async Task<bool> VerifyEmailAsync(string token)
    {
        await Task.Delay(50);

        if (_verificationTokens.TryGetValue(token, out var userId))
        {
            var user = _users.Values.FirstOrDefault(u => u.Id == userId);
            if (user == null) return false;
            user.EmailVerified = true;
            _verificationTokens.Remove(token);
            _logger.LogInformation("Email verified for user {UserId}", userId);
            return true;
        }

        if (_attachTokens.TryGetValue(token, out var attach))
        {
            if (attach.ExpiresAt < DateTime.UtcNow)
            {
                _attachTokens.Remove(token);
                return false;
            }
            var user = _users.Values.FirstOrDefault(u => u.Id == attach.UserId);
            if (user == null) return false;

            if (_users.ContainsKey(attach.Email.ToLower()) &&
                _users[attach.Email.ToLower()].Id != user.Id)
            {
                _attachTokens.Remove(token);
                return false;
            }

            var oldKey = user.Email.ToLower();
            _users.Remove(oldKey);
            user.Email = attach.Email;
            user.PasswordHash = attach.PendingPasswordHash;
            user.EmailVerified = true;
            if (!user.AuthMethods.Contains("local", StringComparer.OrdinalIgnoreCase))
                user.AuthMethods.Add("local");
            _users[attach.Email.ToLower()] = user;

            // Telegram link index still points at the old email key — refresh it.
            if (!string.IsNullOrEmpty(user.TelegramUserId))
                _telegramToUserKey[user.TelegramUserId] = user.Email.ToLower();

            _attachTokens.Remove(token);
            _logger.LogInformation("Attach-email confirmed for user {UserId} → {Email}", user.Id, user.Email);
            return true;
        }

        _logger.LogWarning("Invalid verification token");
        return false;
    }

    public async Task<bool> ForgotPasswordAsync(string email)
    {
        await Task.Delay(100);

        if (!_users.TryGetValue(email.ToLower(), out var user))
        {
            // Don't reveal if email exists
            _logger.LogWarning("Password reset requested for non-existent email {Email}", email);
            return true;
        }

        var resetToken = Guid.NewGuid().ToString();
        _resetTokens[resetToken] = new PasswordResetToken
        {
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        _logger.LogInformation("Password reset token generated for {Email}", email);

        try
        {
            await _emailService.SendPasswordResetEmailAsync(email, user.Name, resetToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send password reset email to {Email}; token remains valid", email);
        }
        return true;
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword)
    {
        await Task.Delay(100);

        if (!_resetTokens.TryGetValue(token, out var resetToken))
        {
            _logger.LogWarning("Invalid reset token");
            return false;
        }

        if (resetToken.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Expired reset token");
            _resetTokens.Remove(token);
            return false;
        }

        var user = _users.Values.FirstOrDefault(u => u.Id == resetToken.UserId);
        if (user == null)
        {
            return false;
        }

        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        _resetTokens.Remove(token);

        // Revoke all refresh tokens for security
        await RevokeAllUserTokensAsync(user.Id);

        _logger.LogInformation("Password reset successful for user {UserId}", user.Id);
        return true;
    }

    public async Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
    {
        await Task.Delay(100);

        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            return false;
        }

        if (!_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            _logger.LogWarning("Change password failed: Invalid current password for {UserId}", userId);
            return false;
        }

        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        
        // Revoke all refresh tokens
        await RevokeAllUserTokensAsync(userId);

        _logger.LogInformation("Password changed for user {UserId}", userId);
        return true;
    }

    public async Task<UserInfo?> GetCurrentUserAsync(string userId)
    {
        await Task.Delay(50);

        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            return null;
        }

        return new UserInfo
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            EmailVerified = user.EmailVerified,
            AuthMethods = user.AuthMethods,
            ProfileImage = user.ProfileImage,
        };
    }

    public async Task<List<AuthMethodDto>> GetAuthMethodsAsync(string userId)
    {
        await Task.Delay(50);

        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            return new List<AuthMethodDto>();
        }

        return user.AuthMethods.Select(method => new AuthMethodDto
        {
            Provider = method,
            LinkedAt = user.CreatedAt,
            LastUsedAt = user.LastLoginAt ?? user.CreatedAt
        }).ToList();
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        await Task.Delay(50);
        _refreshTokens.Remove(refreshToken);
    }

    public async Task RevokeAllUserTokensAsync(string userId)
    {
        await Task.Delay(50);
        
        var tokensToRemove = _refreshTokens.Where(kvp => kvp.Value == userId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var token in tokensToRemove)
        {
            _refreshTokens.Remove(token);
        }

        _logger.LogInformation("Revoked all tokens for user {UserId}", userId);
    }

    private class MockUser
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public bool EmailVerified { get; set; }
        public List<string> AuthMethods { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public int Age { get; set; }
        public string Location { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
        public string? TelegramUserId { get; set; }
        public string? GoogleUserId { get; set; }
        public string ProfileImage { get; set; } = string.Empty;
    }

    private class PasswordResetToken
    {
        public string UserId { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    private static string NormalizeGender(string? gender)
    {
        if (Enum.TryParse<Gender>(gender, ignoreCase: true, out var g))
            return g.ToString();
        return Gender.PreferNotToSay.ToString();
    }

    private class AttachPending
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PendingPasswordHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }
}
