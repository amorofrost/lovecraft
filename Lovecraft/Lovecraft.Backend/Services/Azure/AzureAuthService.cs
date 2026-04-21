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
        IOptions<TelegramAuthOptions> telegramOptions)
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

        _usersTable = tableServiceClient.GetTableClient(TableNames.Users);
        _emailIndexTable = tableServiceClient.GetTableClient(TableNames.UserEmailIndex);
        _telegramIndexTable = tableServiceClient.GetTableClient(TableNames.UserTelegramIndex);
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

        var cfg = await _appConfig.GetConfigAsync();
        string? sourceEventId = null;
        if (!string.IsNullOrWhiteSpace(request.InviteCode))
        {
            var val = await _eventInvites.ValidatePlainCodeAsync(request.InviteCode);
            if (val is null)
                throw new InvalidInviteCodeException();
            sourceEventId = val.EventId;
        }
        else if (cfg.Registration.RequireEventInvite)
        {
            throw new InviteRequiredException();
        }

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
                AuthMethods = new List<string> { "local" }
            },
            ExpiresAt = now.AddMinutes(_jwtSettings.AccessTokenLifetimeMinutes)
        };
    }

    public async Task<AuthResponseDto?> TelegramLoginAsync(TelegramLoginRequestDto request)
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
        UserEntity userEntity;

        try
        {
            var tgIdx = await _telegramIndexTable.GetEntityAsync<UserTelegramIndexEntity>(tgKey, "INDEX");
            var userResponse = await _usersTable.GetEntityAsync<UserEntity>(
                UserEntity.GetPartitionKey(tgIdx.Value.UserId), tgIdx.Value.UserId);
            userEntity = userResponse.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Canonical form is telegram_{id}@telegram.local; if somehow already taken
            // (legacy data, race), fall back to a GUID-suffixed variant so a pre-claimed
            // synthetic address cannot DoS Telegram signups.
            var syntheticEmail = $"telegram_{request.Id}{TelegramSyntheticEmailDomain}";
            var emailLower = syntheticEmail.ToLowerInvariant();

            try
            {
                await _emailIndexTable.GetEntityAsync<UserEmailIndexEntity>(emailLower, "INDEX");
                syntheticEmail = $"telegram_{request.Id}_{Guid.NewGuid():N}{TelegramSyntheticEmailDomain}";
                emailLower = syntheticEmail.ToLowerInvariant();
                _logger.LogWarning("Telegram signup: canonical synthetic email collided for tg {TgId}; using fallback {Email}",
                    request.Id, syntheticEmail);
            }
            catch (RequestFailedException ex2) when (ex2.Status == 404)
            {
                // canonical address is free — keep it
            }

            var userId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            var displayName = string.IsNullOrWhiteSpace(request.LastName)
                ? request.FirstName.Trim()
                : $"{request.FirstName.Trim()} {request.LastName.Trim()}";

            userEntity = new UserEntity
            {
                PartitionKey = UserEntity.GetPartitionKey(userId),
                RowKey = userId,
                Email = syntheticEmail,
                PasswordHash = _passwordHasher.HashPassword(
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))),
                Name = displayName,
                Age = 18,
                Location = "Telegram",
                Gender = "PreferNotToSay",
                Bio = string.Empty,
                ProfileImage = request.PhotoUrl ?? string.Empty,
                EmailVerified = true,
                AuthMethodsJson = JsonSerializer.Serialize(new List<string> { "telegram" }),
                TelegramUserId = tgKey,
                PreferencesJson = JsonSerializer.Serialize(new { AgeRangeMin = 18, AgeRangeMax = 65, MaxDistance = 50, ShowMe = "everyone" }),
                SettingsJson = JsonSerializer.Serialize(new { ProfileVisibility = "public", AnonymousLikes = false, Language = "ru", Notifications = true }),
                CreatedAt = now,
                UpdatedAt = now,
                IsOnline = false,
                LastSeen = now,
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
                await Task.WhenAll(
                    _usersTable.UpsertEntityAsync(userEntity),
                    _emailIndexTable.UpsertEntityAsync(emailIndexEntity),
                    _telegramIndexTable.UpsertEntityAsync(telegramIndexEntity));
            }
            catch (Exception signupEx)
            {
                _logger.LogError(signupEx, "Telegram signup failed for id {Id}", request.Id);
                try { await _telegramIndexTable.DeleteEntityAsync(tgKey, "INDEX"); } catch { /* ignore */ }
                try { await _emailIndexTable.DeleteEntityAsync(emailLower, "INDEX"); } catch { /* ignore */ }
                try { await _usersTable.DeleteEntityAsync(userEntity.PartitionKey, userId); } catch { /* ignore */ }
                throw;
            }

            _logger.LogInformation("Telegram user registered: {UserId}, tg {TgId}", userId, tgKey);
        }

        var issueTime = DateTime.UtcNow;
        var accessToken = _jwtService.GenerateAccessToken(
            userEntity.RowKey, userEntity.Email, userEntity.Name, userEntity.StaffRole ?? "none");
        var refreshToken = _jwtService.GenerateRefreshToken();
        await WriteRefreshTokenAsync(refreshToken, userEntity.RowKey, issueTime);

        var authMethods = JsonSerializer.Deserialize<List<string>>(userEntity.AuthMethodsJson)
            ?? new List<string> { "telegram" };

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
                AuthMethods = authMethods
            },
            ExpiresAt = issueTime.AddMinutes(_jwtSettings.AccessTokenLifetimeMinutes)
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
                AuthMethods = authMethods
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
                AuthMethods = authMethods
            },
            ExpiresAt = now.AddMinutes(_jwtSettings.AccessTokenLifetimeMinutes)
        };
    }

    public async Task<bool> VerifyEmailAsync(string token)
    {
        AuthTokenEntity authToken;
        try
        {
            var response = await _authTokensTable.GetEntityAsync<AuthTokenEntity>(token, "VERIFY");
            authToken = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Invalid verification token");
            return false;
        }

        if (authToken.Used || authToken.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Verification token expired or already used");
            return false;
        }

        // Mark email as verified on the user
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
                AuthMethods = authMethods
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
}
