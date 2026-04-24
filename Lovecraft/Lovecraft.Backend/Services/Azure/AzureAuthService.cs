using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Configuration;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Auth;
using Microsoft.Extensions.Options;

namespace Lovecraft.Backend.Services.Azure;

public class AzureAuthService : IAuthService
{
    /// <summary>
    /// Sentinel domain reserved for Telegram-provisioned synthetic emails. Email/password
    /// registration with this domain is blocked so a malicious actor cannot pre-claim a
    /// Telegram user's synthetic address (which would permanently lock them out of Telegram
    /// sign-in).
    /// </summary>
    private const string TelegramSyntheticEmailDomain = "@telegram.local";

    private readonly TableClient _usersTable;
    private readonly TableClient _emailIndexTable;
    private readonly TableClient _telegramIndexTable;
    private readonly TableClient _googleIndexTable;
    private readonly TableClient _refreshTokensTable;
    private readonly TableClient _authTokensTable;
    private readonly IJwtService _jwtService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AzureAuthService> _logger;
    private readonly IEmailService _emailService;
    private readonly IAppConfigService _appConfig;
    private readonly IEventInviteService _eventInvites;
    private readonly IEventService _events;
    private readonly TelegramAuthOptions _telegramOptions;
    private readonly GoogleAuthOptions _googleOptions;

    public AzureAuthService(
        TableServiceClient tableServiceClient,
        IJwtService jwtService,
        IPasswordHasher passwordHasher,
        JwtSettings jwtSettings,
        ILogger<AzureAuthService> logger,
        IEmailService emailService,
        IAppConfigService appConfig,
        IEventInviteService eventInvites,
        IEventService events,
        IOptions<TelegramAuthOptions> telegramOptions,
        IOptions<GoogleAuthOptions> googleOptions)
    {
        _jwtService = jwtService;
        _passwordHasher = passwordHasher;
        _jwtSettings = jwtSettings;
        _logger = logger;
        _emailService = emailService;
        _appConfig = appConfig;
        _eventInvites = eventInvites;
        _events = events;
        _telegramOptions = telegramOptions.Value;
        _googleOptions = googleOptions.Value;

        _usersTable = tableServiceClient.GetTableClient(TableNames.Users);
        _emailIndexTable = tableServiceClient.GetTableClient(TableNames.UserEmailIndex);
        _telegramIndexTable = tableServiceClient.GetTableClient(TableNames.UserTelegramIndex);
        _googleIndexTable = tableServiceClient.GetTableClient(TableNames.UserGoogleIndex);
        _refreshTokensTable = tableServiceClient.GetTableClient(TableNames.RefreshTokens);
        _authTokensTable = tableServiceClient.GetTableClient(TableNames.AuthTokens);

        InitializeTablesAsync().GetAwaiter().GetResult();
    }

    private async Task InitializeTablesAsync()
    {
        await Task.WhenAll(
            _usersTable.CreateIfNotExistsAsync(),
            _emailIndexTable.CreateIfNotExistsAsync(),
            _telegramIndexTable.CreateIfNotExistsAsync(),
            _googleIndexTable.CreateIfNotExistsAsync(),
            _refreshTokensTable.CreateIfNotExistsAsync(),
            _authTokensTable.CreateIfNotExistsAsync()
        );
    }

    public async Task<AuthResponseDto?> RegisterAsync(RegisterRequestDto request)
    {
        if (request.Email.EndsWith(TelegramSyntheticEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Registration blocked: reserved Telegram synthetic domain {Email}", request.Email);
            return null;
        }

        var sourceEventId = await ResolveInviteSourceAsync(request.InviteCode);

        var emailLower = request.Email.ToLower();

        // Check if email already exists
        try
        {
            await _emailIndexTable.GetEntityAsync<UserEmailIndexEntity>(emailLower, "INDEX");
            _logger.LogWarning("Registration failed: Email already exists {Email}", request.Email);
            return null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Email not found — good, proceed
        }

        var userId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var userEntity = new UserEntity
        {
            PartitionKey = UserEntity.GetPartitionKey(userId),
            RowKey = userId,
            Email = request.Email,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            Name = request.Name,
            Age = request.Age,
            Location = request.Location,
            Gender = request.Gender,
            Bio = request.Bio,
            EmailVerified = false,
            AuthMethodsJson = JsonSerializer.Serialize(new List<string> { "local" }),
            PreferencesJson = JsonSerializer.Serialize(new { AgeRangeMin = 18, AgeRangeMax = 65, MaxDistance = 50, ShowMe = "everyone" }),
            SettingsJson = JsonSerializer.Serialize(new { ProfileVisibility = "public", AnonymousLikes = false, Language = "ru", Notifications = true }),
            CreatedAt = now,
            UpdatedAt = now,
            IsOnline = false,
            LastSeen = now,
            RegistrationSourceEventId = sourceEventId,
            RegistrationSourceRedeemedAtUtc = sourceEventId is not null ? DateTime.UtcNow : null,
        };

        var emailIndexEntity = new UserEmailIndexEntity
        {
            PartitionKey = emailLower,
            RowKey = "INDEX",
            UserId = userId
        };

        try
        {
            await Task.WhenAll(
                _usersTable.UpsertEntityAsync(userEntity),
                _emailIndexTable.UpsertEntityAsync(emailIndexEntity)
            );

            if (sourceEventId is not null && !EventInviteHelpers.IsCampaignEventId(sourceEventId))
                await _events.RegisterForEventAsync(userId, sourceEventId);

            if (!string.IsNullOrWhiteSpace(request.InviteCode))
                await _eventInvites.IncrementRegistrationCountAsync(request.InviteCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed after user row for {Email}", request.Email);
            try
            {
                await _emailIndexTable.DeleteEntityAsync(emailLower, "INDEX");
            }
            catch { /* ignore */ }
            try
            {
                await _usersTable.DeleteEntityAsync(userEntity.PartitionKey, userId);
            }
            catch { /* ignore */ }
            throw;
        }

        // Write email verification token
        var verificationToken = Guid.NewGuid().ToString();
        var authTokenEntity = new AuthTokenEntity
        {
            PartitionKey = verificationToken,
            RowKey = "VERIFY",
            UserId = userId,
            Email = request.Email,
            ExpiresAt = now.AddDays(7),
            Used = false
        };
        await _authTokensTable.UpsertEntityAsync(authTokenEntity);

        _logger.LogInformation("User registered: {UserId}, Email: {Email}", userId, request.Email);

        try
        {
            await _emailService.SendVerificationEmailAsync(request.Email, request.Name, verificationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send verification email to {Email}; token remains valid", request.Email);
        }

        // Generate tokens
        var accessToken = _jwtService.GenerateAccessToken(userId, request.Email, request.Name, "none");
        var refreshToken = _jwtService.GenerateRefreshToken();
        await WriteRefreshTokenAsync(refreshToken, userId, now);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = new UserInfo
            {
                Id = userId,
                Email = request.Email,
                Name = request.Name,
                EmailVerified = false,
                AuthMethods = new List<string> { "local" },
                ProfileImage = string.Empty,
            },
            ExpiresAt = now.AddMinutes(_jwtSettings.AccessTokenLifetimeMinutes)
        };
    }

    public async Task<TelegramLoginResultDto?> TelegramLoginAsync(TelegramLoginRequestDto request)
    {
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
        UserEntity? userEntity = null;

        try
        {
            var tgIdx = await _telegramIndexTable.GetEntityAsync<UserTelegramIndexEntity>(tgKey, "INDEX");
            var userResponse = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(tgIdx.Value.UserId), tgIdx.Value.UserId);
            userEntity = userResponse.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Unknown Telegram id: no user row is created here. The caller must complete a
            // redemption flow (/telegram-register or /telegram-link-login) with the pending ticket.
        }

        if (userEntity is null)
        {
            var tgInfo = new TelegramUserInfoDto
            {
                Id = request.Id,
                FirstName = request.FirstName ?? string.Empty,
                LastName = request.LastName,
                Username = request.Username,
                PhotoUrl = request.PhotoUrl,
            };
            var ticket = _jwtService.GenerateTelegramPendingTicket(tgInfo);
            _logger.LogInformation("Telegram login: pending ticket issued for tg {TgId}", tgKey);
            return new TelegramLoginResultDto
            {
                Status = "pending",
                Ticket = ticket,
                Telegram = tgInfo,
            };
        }

        var auth = await IssueJwtPairAsync(userEntity);
        return new TelegramLoginResultDto { Status = "signedIn", Auth = auth };
    }

    public async Task<AuthResponseDto?> TelegramRegisterAsync(TelegramRegisterRequestDto request)
    {
        var tgInfo = _jwtService.ValidateTelegramPendingTicket(request.Ticket);
        if (tgInfo is null)
        {
            _logger.LogWarning("Telegram register: invalid or expired ticket");
            return null;
        }

        var tgKey = tgInfo.Id.ToString();

        // Prevent double-registration if the id got linked between ticket issuance and redemption.
        try
        {
            await _telegramIndexTable.GetEntityAsync<UserTelegramIndexEntity>(tgKey, "INDEX");
            _logger.LogWarning("Telegram register: tg {TgId} already linked to a user", tgKey);
            return null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // good, not linked — continue
        }

        var sourceEventId = await ResolveInviteSourceAsync(request.InviteCode);

        var syntheticEmail = $"telegram_{tgInfo.Id}{TelegramSyntheticEmailDomain}";
        var emailLower = syntheticEmail.ToLowerInvariant();
        try
        {
            await _emailIndexTable.GetEntityAsync<UserEmailIndexEntity>(emailLower, "INDEX");
            syntheticEmail = $"telegram_{tgInfo.Id}_{Guid.NewGuid():N}{TelegramSyntheticEmailDomain}";
            emailLower = syntheticEmail.ToLowerInvariant();
            _logger.LogWarning("Telegram register: canonical synthetic email collided for tg {TgId}; using fallback", tgKey);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // canonical free
        }

        var userId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var displayName = string.IsNullOrWhiteSpace(request.Name)
            ? (string.IsNullOrWhiteSpace(tgInfo.LastName)
                ? tgInfo.FirstName.Trim()
                : $"{tgInfo.FirstName.Trim()} {tgInfo.LastName!.Trim()}")
            : request.Name.Trim();

        var userEntity = new UserEntity
        {
            PartitionKey = UserEntity.GetPartitionKey(userId),
            RowKey = userId,
            Email = syntheticEmail,
            PasswordHash = _passwordHasher.HashPassword(
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))),
            Name = displayName,
            Age = request.Age > 0 ? request.Age : 18,
            Location = string.IsNullOrWhiteSpace(request.Location) ? "Telegram" : request.Location,
            Gender = string.IsNullOrWhiteSpace(request.Gender) ? "PreferNotToSay" : request.Gender,
            Bio = request.Bio ?? string.Empty,
            ProfileImage = tgInfo.PhotoUrl ?? string.Empty,
            EmailVerified = true,
            AuthMethodsJson = JsonSerializer.Serialize(new List<string> { "telegram" }),
            TelegramUserId = tgKey,
            PreferencesJson = JsonSerializer.Serialize(new { AgeRangeMin = 18, AgeRangeMax = 65, MaxDistance = 50, ShowMe = "everyone" }),
            SettingsJson = JsonSerializer.Serialize(new { ProfileVisibility = "public", AnonymousLikes = false, Language = "ru", Notifications = true }),
            CreatedAt = now,
            UpdatedAt = now,
            IsOnline = false,
            LastSeen = now,
            RegistrationSourceEventId = sourceEventId,
            RegistrationSourceRedeemedAtUtc = sourceEventId is not null ? now : null,
        };

        var emailIndexEntity = new UserEmailIndexEntity
        {
            PartitionKey = emailLower,
            RowKey = "INDEX",
            UserId = userId
        };
        var telegramIndexEntity = new UserTelegramIndexEntity
        {
            PartitionKey = tgKey,
            RowKey = "INDEX",
            UserId = userId
        };

        try
        {
            // AddEntityAsync on the telegram index gives us atomic "insert if absent"; races
            // between concurrent pending tickets lose here instead of silently creating dupes.
            await _telegramIndexTable.AddEntityAsync(telegramIndexEntity);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            _logger.LogWarning("Telegram register: race detected, tg {TgId} already linked", tgKey);
            return null;
        }

        try
        {
            await Task.WhenAll(
                _usersTable.UpsertEntityAsync(userEntity),
                _emailIndexTable.UpsertEntityAsync(emailIndexEntity));

            if (sourceEventId is not null && !EventInviteHelpers.IsCampaignEventId(sourceEventId))
                await _events.RegisterForEventAsync(userId, sourceEventId);

            if (!string.IsNullOrWhiteSpace(request.InviteCode))
                await _eventInvites.IncrementRegistrationCountAsync(request.InviteCode);
        }
        catch (Exception signupEx)
        {
            _logger.LogError(signupEx, "Telegram register failed after tg index write for tg {TgId}", tgKey);
            try { await _telegramIndexTable.DeleteEntityAsync(tgKey, "INDEX"); } catch { /* ignore */ }
            try { await _emailIndexTable.DeleteEntityAsync(emailLower, "INDEX"); } catch { /* ignore */ }
            try { await _usersTable.DeleteEntityAsync(userEntity.PartitionKey, userId); } catch { /* ignore */ }
            throw;
        }

        _logger.LogInformation("Telegram user registered: {UserId}, tg {TgId}", userId, tgKey);
        return await IssueJwtPairAsync(userEntity);
    }

    public async Task<AuthResponseDto?> TelegramLinkLoginAsync(TelegramLinkLoginRequestDto request)
    {
        var tgInfo = _jwtService.ValidateTelegramPendingTicket(request.Ticket);
        if (tgInfo is null)
        {
            _logger.LogWarning("Telegram link-login: invalid or expired ticket");
            return null;
        }

        var emailLower = request.Email.ToLower();
        UserEmailIndexEntity indexEntity;
        try
        {
            var idxResp = await _emailIndexTable.GetEntityAsync<UserEmailIndexEntity>(emailLower, "INDEX");
            indexEntity = idxResp.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Telegram link-login: user not found for {Email}", request.Email);
            return null;
        }

        UserEntity userEntity;
        try
        {
            var resp = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(indexEntity.UserId), indexEntity.UserId);
            userEntity = resp.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }

        if (!_passwordHasher.VerifyPassword(request.Password, userEntity.PasswordHash))
        {
            _logger.LogWarning("Telegram link-login: invalid password for {Email}", request.Email);
            return null;
        }

        if (!userEntity.EmailVerified)
        {
            _logger.LogWarning("Telegram link-login: email not verified for {Email}", request.Email);
            return null;
        }

        if (!await AttachTelegramToUserAsync(userEntity, tgInfo))
            return null;

        _logger.LogInformation("Telegram linked to existing user {UserId} via link-login", userEntity.RowKey);
        return await IssueJwtPairAsync(userEntity);
    }

    public async Task<AuthResponseDto?> TelegramLinkAsync(string userId, string ticket)
    {
        var tgInfo = _jwtService.ValidateTelegramPendingTicket(ticket);
        if (tgInfo is null)
        {
            _logger.LogWarning("Telegram link: invalid or expired ticket for user {UserId}", userId);
            return null;
        }

        UserEntity userEntity;
        try
        {
            var resp = await _usersTable.GetEntityAsync<UserEntity>(UserEntity.GetPartitionKey(userId), userId);
            userEntity = resp.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }

        if (!await AttachTelegramToUserAsync(userEntity, tgInfo))
            return null;

        _logger.LogInformation("Telegram linked to existing user {UserId} (authenticated)", userId);
        return await IssueJwtPairAsync(userEntity);
    }

    public async Task<TelegramMiniAppLoginResultDto?> MiniAppLoginAsync(TelegramMiniAppLoginRequestDto request)
    {
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
        UserEntity? userEntity = null;
        try
        {
            var tgIdx = await _telegramIndexTable.GetEntityAsync<UserTelegramIndexEntity>(tgKey, "INDEX");
            var userResp = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(tgIdx.Value.UserId), tgIdx.Value.UserId);
            userEntity = userResp.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Unknown tg id — frontend renders inline onboarding.
        }

        if (userEntity is null)
        {
            _logger.LogInformation("Mini app login: needsRegistration for tg {TgId}", tgKey);
            return new TelegramMiniAppLoginResultDto { Status = "needsRegistration", Telegram = tgInfo };
        }

        var auth = await IssueJwtPairAsync(userEntity);
        return new TelegramMiniAppLoginResultDto { Status = "signedIn", Auth = auth, Telegram = tgInfo };
    }

    public async Task<AuthResponseDto?> MiniAppRegisterAsync(TelegramMiniAppRegisterRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(_telegramOptions.BotToken)) return null;
        var tgInfo = TelegramInitDataValidator.Validate(_telegramOptions.BotToken, request.InitData);
        if (tgInfo is null)
        {
            _logger.LogWarning("Mini app register: invalid initData");
            return null;
        }

        // Bridge to the existing pending-ticket flow: mint a short-lived ticket and reuse
        // TelegramRegisterAsync so the whole create-user pipeline (invite gate, synthetic email,
        // atomic tg index, event registration) stays in one place.
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
        if (tgInfo is null)
        {
            _logger.LogWarning("Mini app link-login: invalid initData");
            return null;
        }

        var ticket = _jwtService.GenerateTelegramPendingTicket(tgInfo);
        return await TelegramLinkLoginAsync(new TelegramLinkLoginRequestDto
        {
            Email = request.Email,
            Password = request.Password,
            Ticket = ticket,
        });
    }

    public async Task<AttachEmailResult> RequestEmailAttachAsync(string userId, string email, string password)
    {
        if (email.EndsWith(TelegramSyntheticEmailDomain, StringComparison.OrdinalIgnoreCase))
            return AttachEmailResult.ReservedDomain;

        UserEntity userEntity;
        try
        {
            var resp = await _usersTable.GetEntityAsync<UserEntity>(UserEntity.GetPartitionKey(userId), userId);
            userEntity = resp.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return AttachEmailResult.UserNotFound;
        }

        var methods = JsonSerializer.Deserialize<List<string>>(userEntity.AuthMethodsJson) ?? new List<string>();
        if (methods.Contains("local", StringComparer.OrdinalIgnoreCase))
            return AttachEmailResult.AlreadyHasLocal;

        var emailLower = email.ToLower();
        try
        {
            await _emailIndexTable.GetEntityAsync<UserEmailIndexEntity>(emailLower, "INDEX");
            return AttachEmailResult.EmailAlreadyTaken;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // free — continue
        }

        // Clean up any earlier ATTACH tokens for this user (one pending attach at a time).
        var existingAttaches = _authTokensTable.QueryAsync<AuthTokenEntity>(filter: $"RowKey eq 'ATTACH' and UserId eq '{userId}'");
        await foreach (var t in existingAttaches)
        {
            try { await _authTokensTable.DeleteEntityAsync(t.PartitionKey, t.RowKey); } catch { /* ignore */ }
        }

        var token = Guid.NewGuid().ToString();
        var attach = new AuthTokenEntity
        {
            PartitionKey = token,
            RowKey = "ATTACH",
            UserId = userId,
            Email = email,
            PendingPasswordHash = _passwordHasher.HashPassword(password),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            Used = false,
        };
        await _authTokensTable.UpsertEntityAsync(attach);

        try
        {
            await _emailService.SendVerificationEmailAsync(email, userEntity.Name, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send attach-email verification to {Email}; token remains valid", email);
        }

        _logger.LogInformation("Attach-email requested for user {UserId} → {Email}", userId, email);
        return AttachEmailResult.Ok;
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
        var emailLower = google.Email.Trim().ToLowerInvariant();

        try
        {
            var gIdx = await _googleIndexTable.GetEntityAsync<UserGoogleIndexEntity>(sub, "INDEX");
            var uResp = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(gIdx.Value.UserId), gIdx.Value.UserId);
            return new GoogleLoginResultDto
            {
                Status = "signedIn",
                Auth = await IssueJwtPairAsync(uResp.Value)
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { /* not in google index */ }

        try
        {
            var emailIdx = await _emailIndexTable.GetEntityAsync<UserEmailIndexEntity>(emailLower, "INDEX");
            var uResp = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(emailIdx.Value.UserId), emailIdx.Value.UserId);
            var u = uResp.Value;

            if (string.IsNullOrEmpty(u.GoogleUserId))
            {
                if (!await AttachGoogleToUserAsync(u, google))
                {
                    return new GoogleLoginResultDto
                    {
                        Status = "emailConflict",
                        Message = "Could not link Google to this account."
                    };
                }
                var reloaded = await _usersTable.GetEntityAsync<UserEntity>(
                    UserEntity.GetPartitionKey(u.RowKey), u.RowKey);
                return new GoogleLoginResultDto
                {
                    Status = "signedIn",
                    Auth = await IssueJwtPairAsync(reloaded.Value)
                };
            }

            if (string.Equals(u.GoogleUserId, sub, StringComparison.Ordinal))
            {
                await EnsureGoogleIndexAsync(sub, u.RowKey);
                var reloaded = await _usersTable.GetEntityAsync<UserEntity>(
                    UserEntity.GetPartitionKey(u.RowKey), u.RowKey);
                return new GoogleLoginResultDto
                {
                    Status = "signedIn",
                    Auth = await IssueJwtPairAsync(reloaded.Value)
                };
            }

            return new GoogleLoginResultDto
            {
                Status = "emailConflict",
                Message = "This email is already associated with a different Google account."
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { /* no such email */ }

        var ticket = _jwtService.GenerateGooglePendingTicket(google);
        _logger.LogInformation("Google login: pending registration for {Email}", emailLower);
        return new GoogleLoginResultDto
        {
            Status = "pending",
            Ticket = ticket,
            Google = google
        };
    }

    public async Task<AuthResponseDto?> GoogleRegisterAsync(GoogleRegisterRequestDto request)
    {
        var gInfo = _jwtService.ValidateGooglePendingTicket(request.Ticket);
        if (gInfo is null)
        {
            _logger.LogWarning("Google register: invalid or expired ticket");
            return null;
        }

        if (gInfo.Email.EndsWith(TelegramSyntheticEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Google register: reserved email domain {Email}", gInfo.Email);
            return null;
        }

        try
        {
            await _googleIndexTable.GetEntityAsync<UserGoogleIndexEntity>(gInfo.Sub, "INDEX");
            _logger.LogWarning("Google register: sub {Sub} already registered", gInfo.Sub);
            return null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        var emailLower = gInfo.Email.Trim().ToLowerInvariant();
        try
        {
            await _emailIndexTable.GetEntityAsync<UserEmailIndexEntity>(emailLower, "INDEX");
            _logger.LogWarning("Google register: email {Email} already taken", gInfo.Email);
            return null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        var sourceEventId = await ResolveInviteSourceAsync(request.InviteCode);

        var userId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var displayName = string.IsNullOrWhiteSpace(request.Name) ? gInfo.Name.Trim() : request.Name.Trim();
        if (string.IsNullOrEmpty(displayName)) displayName = gInfo.Email;

        var userEntity = new UserEntity
        {
            PartitionKey = UserEntity.GetPartitionKey(userId),
            RowKey = userId,
            Email = gInfo.Email.Trim(),
            PasswordHash = _passwordHasher.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))),
            Name = displayName,
            Age = request.Age > 0 ? request.Age : 18,
            Location = string.IsNullOrWhiteSpace(request.Location) ? "—" : request.Location,
            Gender = string.IsNullOrWhiteSpace(request.Gender) ? "PreferNotToSay" : request.Gender,
            Bio = request.Bio ?? string.Empty,
            ProfileImage = gInfo.PictureUrl ?? string.Empty,
            EmailVerified = gInfo.EmailVerified,
            GoogleUserId = gInfo.Sub,
            AuthMethodsJson = JsonSerializer.Serialize(new List<string> { "google" }),
            PreferencesJson = JsonSerializer.Serialize(new { AgeRangeMin = 18, AgeRangeMax = 65, MaxDistance = 50, ShowMe = "everyone" }),
            SettingsJson = JsonSerializer.Serialize(new { ProfileVisibility = "public", AnonymousLikes = false, Language = "ru", Notifications = true }),
            CreatedAt = now,
            UpdatedAt = now,
            IsOnline = false,
            LastSeen = now,
            RegistrationSourceEventId = sourceEventId,
            RegistrationSourceRedeemedAtUtc = sourceEventId is not null ? now : null,
        };

        var emailIndexEntity = new UserEmailIndexEntity
        {
            PartitionKey = emailLower,
            RowKey = "INDEX",
            UserId = userId
        };
        var googleIndexEntity = new UserGoogleIndexEntity
        {
            PartitionKey = gInfo.Sub,
            RowKey = "INDEX",
            UserId = userId
        };

        try
        {
            await _googleIndexTable.AddEntityAsync(googleIndexEntity);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            _logger.LogWarning("Google register: race on google sub {Sub}", gInfo.Sub);
            return null;
        }

        try
        {
            // AddEntityAsync (not Upsert) on the email index so a concurrent registration
            // with the same email fails with 409 rather than silently overwriting.
            await Task.WhenAll(
                _usersTable.UpsertEntityAsync(userEntity),
                _emailIndexTable.AddEntityAsync(emailIndexEntity));

            if (sourceEventId is not null && !EventInviteHelpers.IsCampaignEventId(sourceEventId))
                await _events.RegisterForEventAsync(userId, sourceEventId);

            if (!string.IsNullOrWhiteSpace(request.InviteCode))
                await _eventInvites.IncrementRegistrationCountAsync(request.InviteCode);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Another registration claimed the email between our read-check and write.
            _logger.LogWarning("Google register: email index conflict for {Email}", emailLower);
            try { await _googleIndexTable.DeleteEntityAsync(gInfo.Sub, "INDEX"); } catch { /* */ }
            try { await _usersTable.DeleteEntityAsync(userEntity.PartitionKey, userId); } catch { /* */ }
            return null;
        }
        catch (Exception signupEx)
        {
            _logger.LogError(signupEx, "Google register failed after index write for sub {Sub}", gInfo.Sub);
            try { await _googleIndexTable.DeleteEntityAsync(gInfo.Sub, "INDEX"); } catch { /* */ }
            try { await _emailIndexTable.DeleteEntityAsync(emailLower, "INDEX"); } catch { /* */ }
            try { await _usersTable.DeleteEntityAsync(userEntity.PartitionKey, userId); } catch { /* */ }
            throw;
        }

        _logger.LogInformation("Google user registered: {UserId}, {Email}", userId, gInfo.Email);
        return await IssueJwtPairAsync(
            (await _usersTable.GetEntityAsync<UserEntity>(userEntity.PartitionKey, userId)).Value);
    }

    private async Task EnsureGoogleIndexAsync(string sub, string userId)
    {
        var entity = new UserGoogleIndexEntity
        {
            PartitionKey = sub,
            RowKey = "INDEX",
            UserId = userId
        };
        try
        {
            await _googleIndexTable.AddEntityAsync(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 409) { }
    }

    private async Task<bool> AttachGoogleToUserAsync(UserEntity userEntity, GoogleUserInfoDto gInfo)
    {
        var sub = gInfo.Sub;
        try
        {
            var existing = await _googleIndexTable.GetEntityAsync<UserGoogleIndexEntity>(sub, "INDEX");
            if (existing.Value.UserId != userEntity.RowKey)
            {
                _logger.LogWarning("Google link: sub {Sub} already linked to another user", sub);
                return false;
            }
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        var gIdx = new UserGoogleIndexEntity
        {
            PartitionKey = sub,
            RowKey = "INDEX",
            UserId = userEntity.RowKey
        };
        try
        {
            await _googleIndexTable.AddEntityAsync(gIdx);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false;
        }

        var methods = JsonSerializer.Deserialize<List<string>>(userEntity.AuthMethodsJson) ?? new List<string>();
        if (!methods.Contains("google", StringComparer.OrdinalIgnoreCase))
            methods.Add("google");
        userEntity.AuthMethodsJson = JsonSerializer.Serialize(methods);
        userEntity.GoogleUserId = sub;
        if (string.IsNullOrWhiteSpace(userEntity.ProfileImage) && !string.IsNullOrEmpty(gInfo.PictureUrl))
            userEntity.ProfileImage = gInfo.PictureUrl!;
        userEntity.UpdatedAt = DateTime.UtcNow;
        await _usersTable.UpdateEntityAsync(userEntity, userEntity.ETag, TableUpdateMode.Replace);
        return true;
    }

    /// <summary>Set TelegramUserId + append "telegram" to AuthMethods + write tg index (atomic insert).</summary>
    private async Task<bool> AttachTelegramToUserAsync(UserEntity userEntity, TelegramUserInfoDto tgInfo)
    {
        var tgKey = tgInfo.Id.ToString();

        // Refuse if a different user already owns this tg id.
        try
        {
            var existing = await _telegramIndexTable.GetEntityAsync<UserTelegramIndexEntity>(tgKey, "INDEX");
            if (existing.Value.UserId != userEntity.RowKey)
            {
                _logger.LogWarning("Telegram link: tg {TgId} already linked to a different user", tgKey);
                return false;
            }
            // Already linked to this same user — nothing to write.
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // good, free to claim
        }

        var tgIdxEntity = new UserTelegramIndexEntity
        {
            PartitionKey = tgKey,
            RowKey = "INDEX",
            UserId = userEntity.RowKey,
        };
        try
        {
            await _telegramIndexTable.AddEntityAsync(tgIdxEntity);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            _logger.LogWarning("Telegram link: race on tg {TgId}", tgKey);
            return false;
        }

        var methods = JsonSerializer.Deserialize<List<string>>(userEntity.AuthMethodsJson) ?? new List<string>();
        if (!methods.Contains("telegram", StringComparer.OrdinalIgnoreCase))
            methods.Add("telegram");
        userEntity.AuthMethodsJson = JsonSerializer.Serialize(methods);
        userEntity.TelegramUserId = tgKey;
        userEntity.UpdatedAt = DateTime.UtcNow;
        await _usersTable.UpdateEntityAsync(userEntity, userEntity.ETag);
        return true;
    }

    private async Task<AuthResponseDto> IssueJwtPairAsync(UserEntity userEntity)
    {
        var now = DateTime.UtcNow;
        var accessToken = _jwtService.GenerateAccessToken(
            userEntity.RowKey, userEntity.Email, userEntity.Name, userEntity.StaffRole ?? "none");
        var refreshToken = _jwtService.GenerateRefreshToken();
        await WriteRefreshTokenAsync(refreshToken, userEntity.RowKey, now);

        var authMethods = JsonSerializer.Deserialize<List<string>>(userEntity.AuthMethodsJson) ?? new List<string>();

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = new UserInfo
            {
                Id = userEntity.RowKey,
                Email = userEntity.Email,
                Name = userEntity.Name,
                EmailVerified = userEntity.EmailVerified,
                AuthMethods = authMethods,
                ProfileImage = userEntity.ProfileImage,
            },
            ExpiresAt = now.AddMinutes(_jwtSettings.AccessTokenLifetimeMinutes)
        };
    }

    public async Task<AuthResponseDto?> LoginAsync(LoginRequestDto request)
    {
        var emailLower = request.Email.ToLower();

        // Look up userId from email index
        UserEmailIndexEntity indexEntity;
        try
        {
            var indexResponse = await _emailIndexTable.GetEntityAsync<UserEmailIndexEntity>(emailLower, "INDEX");
            indexEntity = indexResponse.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Login failed: User not found {Email}", request.Email);
            return null;
        }

        // Load the user
        UserEntity userEntity;
        try
        {
            var userResponse = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(indexEntity.UserId), indexEntity.UserId);
            userEntity = userResponse.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Login failed: User entity not found for {Email}", request.Email);
            return null;
        }

        if (!_passwordHasher.VerifyPassword(request.Password, userEntity.PasswordHash))
        {
            _logger.LogWarning("Login failed: Invalid password for {Email}", request.Email);
            return null;
        }

        if (!userEntity.EmailVerified)
        {
            _logger.LogWarning("Login failed: Email not verified for {Email}", request.Email);
            return null;
        }

        var now = DateTime.UtcNow;
        var accessToken = _jwtService.GenerateAccessToken(
            userEntity.RowKey, userEntity.Email, userEntity.Name, userEntity.StaffRole ?? "none");
        var refreshToken = _jwtService.GenerateRefreshToken();
        await WriteRefreshTokenAsync(refreshToken, userEntity.RowKey, now);

        var authMethods = JsonSerializer.Deserialize<List<string>>(userEntity.AuthMethodsJson) ?? new List<string> { "local" };

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = new UserInfo
            {
                Id = userEntity.RowKey,
                Email = userEntity.Email,
                Name = userEntity.Name,
                EmailVerified = userEntity.EmailVerified,
                AuthMethods = authMethods,
                ProfileImage = userEntity.ProfileImage,
            },
            ExpiresAt = now.AddMinutes(_jwtSettings.AccessTokenLifetimeMinutes)
        };
    }

    public async Task<AuthResponseDto?> RefreshTokenAsync(string refreshToken)
    {
        var tokenHash = HashToken(refreshToken);

        RefreshTokenEntity tokenEntity;
        try
        {
            var response = await _refreshTokensTable.GetEntityAsync<RefreshTokenEntity>(tokenHash, "TOKEN");
            tokenEntity = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Refresh token not found");
            return null;
        }

        if (tokenEntity.IsRevoked || tokenEntity.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Refresh token expired or revoked for user {UserId}", tokenEntity.UserId);
            return null;
        }

        UserEntity userEntity;
        try
        {
            var userResponse = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(tokenEntity.UserId), tokenEntity.UserId);
            userEntity = userResponse.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }

        // Delete old refresh token
        await _refreshTokensTable.DeleteEntityAsync(tokenHash, "TOKEN");

        // Issue new tokens
        var now = DateTime.UtcNow;
        var newAccessToken = _jwtService.GenerateAccessToken(
            userEntity.RowKey, userEntity.Email, userEntity.Name, userEntity.StaffRole ?? "none");
        var newRefreshToken = _jwtService.GenerateRefreshToken();
        await WriteRefreshTokenAsync(newRefreshToken, userEntity.RowKey, now);

        var authMethods = JsonSerializer.Deserialize<List<string>>(userEntity.AuthMethodsJson) ?? new List<string> { "local" };

        return new AuthResponseDto
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            User = new UserInfo
            {
                Id = userEntity.RowKey,
                Email = userEntity.Email,
                Name = userEntity.Name,
                EmailVerified = userEntity.EmailVerified,
                AuthMethods = authMethods,
                ProfileImage = userEntity.ProfileImage,
            },
            ExpiresAt = now.AddMinutes(_jwtSettings.AccessTokenLifetimeMinutes)
        };
    }

    public async Task<bool> VerifyEmailAsync(string token)
    {
        AuthTokenEntity authToken;
        string rowKey;

        // Accept both a fresh-signup VERIFY row and a Telegram-only ATTACH row. ATTACH carries a
        // pending email+password swap that we only apply once the verification link is clicked.
        try
        {
            var response = await _authTokensTable.GetEntityAsync<AuthTokenEntity>(token, "VERIFY");
            authToken = response.Value;
            rowKey = "VERIFY";
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            try
            {
                var response = await _authTokensTable.GetEntityAsync<AuthTokenEntity>(token, "ATTACH");
                authToken = response.Value;
                rowKey = "ATTACH";
            }
            catch (RequestFailedException ex2) when (ex2.Status == 404)
            {
                _logger.LogWarning("Invalid verification token");
                return false;
            }
        }

        if (authToken.Used || authToken.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Verification token expired or already used");
            return false;
        }

        if (rowKey == "VERIFY")
        {
            try
            {
                var userResponse = await _usersTable.GetEntityAsync<UserEntity>(
                    UserEntity.GetPartitionKey(authToken.UserId), authToken.UserId);
                var userEntity = userResponse.Value;
                userEntity.EmailVerified = true;
                userEntity.UpdatedAt = DateTime.UtcNow;
                await _usersTable.UpdateEntityAsync(userEntity, userEntity.ETag);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return false;
            }

            await _authTokensTable.DeleteEntityAsync(token, "VERIFY");
            _logger.LogInformation("Email verified for user {UserId}", authToken.UserId);
            return true;
        }

        // ATTACH: swap the synthetic @telegram.local email for the verified real email and
        // append "local" to AuthMethods. Password becomes the PendingPasswordHash captured at
        // attach-request time.
        UserEntity user;
        try
        {
            var userResponse = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(authToken.UserId), authToken.UserId);
            user = userResponse.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }

        var newEmailLower = authToken.Email.ToLower();
        // Recheck uniqueness in case someone else registered the email while this token was pending.
        try
        {
            var collide = await _emailIndexTable.GetEntityAsync<UserEmailIndexEntity>(newEmailLower, "INDEX");
            if (collide.Value.UserId != user.RowKey)
            {
                _logger.LogWarning("Attach-email: {Email} now taken by another user; aborting", authToken.Email);
                await _authTokensTable.DeleteEntityAsync(token, "ATTACH");
                return false;
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // free — continue
        }

        var oldEmailLower = user.Email.ToLower();

        var newIndex = new UserEmailIndexEntity
        {
            PartitionKey = newEmailLower,
            RowKey = "INDEX",
            UserId = user.RowKey,
        };
        try
        {
            await _emailIndexTable.AddEntityAsync(newIndex);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            _logger.LogWarning("Attach-email: {Email} collided on write; aborting", authToken.Email);
            await _authTokensTable.DeleteEntityAsync(token, "ATTACH");
            return false;
        }

        var methods = JsonSerializer.Deserialize<List<string>>(user.AuthMethodsJson) ?? new List<string>();
        if (!methods.Contains("local", StringComparer.OrdinalIgnoreCase))
            methods.Add("local");

        user.Email = authToken.Email;
        user.PasswordHash = authToken.PendingPasswordHash;
        user.AuthMethodsJson = JsonSerializer.Serialize(methods);
        user.EmailVerified = true;
        user.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _usersTable.UpdateEntityAsync(user, user.ETag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attach-email: failed to update user {UserId}; rolling back index", user.RowKey);
            try { await _emailIndexTable.DeleteEntityAsync(newEmailLower, "INDEX"); } catch { /* ignore */ }
            return false;
        }

        if (!string.IsNullOrEmpty(oldEmailLower) && oldEmailLower != newEmailLower)
        {
            try { await _emailIndexTable.DeleteEntityAsync(oldEmailLower, "INDEX"); }
            catch (RequestFailedException ex) when (ex.Status == 404) { /* already gone */ }
        }

        await _authTokensTable.DeleteEntityAsync(token, "ATTACH");
        _logger.LogInformation("Attach-email confirmed for user {UserId} → {Email}", user.RowKey, authToken.Email);
        return true;
    }

    public async Task<bool> ForgotPasswordAsync(string email)
    {
        var emailLower = email.ToLower();
        string userId;
        try
        {
            var indexResponse = await _emailIndexTable.GetEntityAsync<UserEmailIndexEntity>(emailLower, "INDEX");
            userId = indexResponse.Value.UserId;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Password reset requested for non-existent email {Email}", email);
            return true;
        }

        // Load the full user entity to get Name for the personalised email greeting
        UserEntity userEntity;
        try
        {
            var userResponse = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(userId), userId);
            userEntity = userResponse.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Password reset: index entry exists for {Email} but user row missing (data inconsistency)", email);
            return true; // anti-enumeration: don't write token, return success
        }

        var resetToken = Guid.NewGuid().ToString();
        var authTokenEntity = new AuthTokenEntity
        {
            PartitionKey = resetToken,
            RowKey = "RESET",
            UserId = userId,
            Email = email,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Used = false
        };
        await _authTokensTable.UpsertEntityAsync(authTokenEntity);

        _logger.LogInformation("Password reset token generated for {Email}", email);

        try
        {
            await _emailService.SendPasswordResetEmailAsync(email, userEntity.Name, resetToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send password reset email to {Email}; token remains valid", email);
        }

        return true;
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword)
    {
        AuthTokenEntity authToken;
        try
        {
            var response = await _authTokensTable.GetEntityAsync<AuthTokenEntity>(token, "RESET");
            authToken = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Invalid reset token");
            return false;
        }

        if (authToken.Used || authToken.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Reset token expired or already used");
            await _authTokensTable.DeleteEntityAsync(token, "RESET");
            return false;
        }

        // Consume the token before updating the user to prevent replay attacks.
        // If the user update subsequently fails, the token is already invalidated
        // and the user must request a new reset link.
        await _authTokensTable.DeleteEntityAsync(token, "RESET");

        try
        {
            var userResponse = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(authToken.UserId), authToken.UserId);
            var userEntity = userResponse.Value;
            userEntity.PasswordHash = _passwordHasher.HashPassword(newPassword);
            userEntity.UpdatedAt = DateTime.UtcNow;
            await _usersTable.UpdateEntityAsync(userEntity, userEntity.ETag);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }

        await RevokeAllUserTokensAsync(authToken.UserId);

        _logger.LogInformation("Password reset successful for user {UserId}", authToken.UserId);
        return true;
    }

    public async Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
    {
        UserEntity userEntity;
        try
        {
            var userResponse = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(userId), userId);
            userEntity = userResponse.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }

        if (!_passwordHasher.VerifyPassword(currentPassword, userEntity.PasswordHash))
        {
            _logger.LogWarning("Change password failed: Invalid current password for {UserId}", userId);
            return false;
        }

        userEntity.PasswordHash = _passwordHasher.HashPassword(newPassword);
        userEntity.UpdatedAt = DateTime.UtcNow;
        await _usersTable.UpdateEntityAsync(userEntity, userEntity.ETag);
        await RevokeAllUserTokensAsync(userId);

        _logger.LogInformation("Password changed for user {UserId}", userId);
        return true;
    }

    public async Task<UserInfo?> GetCurrentUserAsync(string userId)
    {
        try
        {
            var userResponse = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(userId), userId);
            var userEntity = userResponse.Value;
            var authMethods = JsonSerializer.Deserialize<List<string>>(userEntity.AuthMethodsJson) ?? new List<string> { "local" };

            return new UserInfo
            {
                Id = userEntity.RowKey,
                Email = userEntity.Email,
                Name = userEntity.Name,
                EmailVerified = userEntity.EmailVerified,
                AuthMethods = authMethods,
                ProfileImage = userEntity.ProfileImage,
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<AuthMethodDto>> GetAuthMethodsAsync(string userId)
    {
        try
        {
            var userResponse = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(userId), userId);
            var userEntity = userResponse.Value;
            var authMethods = JsonSerializer.Deserialize<List<string>>(userEntity.AuthMethodsJson) ?? new List<string> { "local" };

            return authMethods.Select(method => new AuthMethodDto
            {
                Provider = method,
                LinkedAt = userEntity.CreatedAt,
                LastUsedAt = userEntity.UpdatedAt
            }).ToList();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new List<AuthMethodDto>();
        }
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        var tokenHash = HashToken(refreshToken);
        try
        {
            await _refreshTokensTable.DeleteEntityAsync(tokenHash, "TOKEN");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone
        }
    }

    public async Task RevokeAllUserTokensAsync(string userId)
    {
        // Table scan — acceptable: infrequent operation, small table
        var tokens = _refreshTokensTable.QueryAsync<RefreshTokenEntity>(
            filter: $"UserId eq '{userId}'");

        var deleteTasks = new List<Task>();
        await foreach (var token in tokens)
        {
            deleteTasks.Add(_refreshTokensTable.DeleteEntityAsync(token.PartitionKey, token.RowKey));
        }

        await Task.WhenAll(deleteTasks);
        _logger.LogInformation("Revoked all tokens for user {UserId}", userId);
    }

    private async Task WriteRefreshTokenAsync(string refreshToken, string userId, DateTime now)
    {
        var tokenHash = HashToken(refreshToken);
        var entity = new RefreshTokenEntity
        {
            PartitionKey = tokenHash,
            RowKey = "TOKEN",
            UserId = userId,
            ExpiresAt = now.AddDays(_jwtSettings.RefreshTokenLifetimeDays),
            CreatedAt = now,
            IsRevoked = false
        };
        await _refreshTokensTable.UpsertEntityAsync(entity);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLower();
    }

    /// <summary>
    /// Validates an optional invite code against the current <c>require_event_invite</c> appconfig
    /// flag. Returns the source event id (or null when no code supplied and none required). Throws
    /// <see cref="InvalidInviteCodeException"/> for bad codes and <see cref="InviteRequiredException"/>
    /// when a code is required but missing.
    /// </summary>
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
}
