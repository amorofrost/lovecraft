using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class UserEntity : ITableEntity
{
    // PK = "user#{userId[0].lower()}", RK = userId
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Email { get; set; } = string.Empty;
    /// <summary>Original-case account name as typed at registration. Empty for legacy GUID-userId rows.</summary>
    public string AccountNameDisplay { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Bio { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string SecondaryCountry { get; set; } = string.Empty;
    public string SecondaryRegion { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string ProfileImage { get; set; } = string.Empty;
    public string ImagesJson { get; set; } = "[]";
    public string PromptsJson { get; set; } = "[]";
    public bool IsOnline { get; set; }
    public DateTime LastSeen { get; set; }
    public bool EmailVerified { get; set; }
    public string AuthMethodsJson { get; set; } = "[]";
    public string PreferencesJson { get; set; } = "{}";
    public string SettingsJson { get; set; } = "{}";
    public string FavoriteSongJson { get; set; } = string.Empty;
    public int ReplyCount { get; set; }
    public int LikesReceived { get; set; }
    public int EventsAttended { get; set; }
    public int MatchCount { get; set; }
    public string StaffRole { get; set; } = "none";
    public string? RankOverride { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Set when the account was created using a valid event invite code (immutable).</summary>
    public string? RegistrationSourceEventId { get; set; }

    public DateTime? RegistrationSourceRedeemedAtUtc { get; set; }

    /// <summary>Telegram user id (string) when linked via Login Widget / Mini App; empty otherwise.</summary>
    public string TelegramUserId { get; set; } = string.Empty;

    /// <summary>Google <c>sub</c> when the account uses Google sign-in; empty otherwise.</summary>
    public string GoogleUserId { get; set; } = string.Empty;

    /// <summary>True when the row was created by admin attendee pre-registration.
    /// Combined with an empty <see cref="TelegramUserId"/> this marks an unclaimed shell
    /// account that may be claimed by a matching Telegram username on first sign-in.</summary>
    public bool PreRegistered { get; set; }

    /// <summary>Instagram username (without @), optional.</summary>
    public string InstagramHandle { get; set; } = string.Empty;

    public static string GetPartitionKey(string userId) =>
        $"user-{userId[0].ToString().ToLower()}";
}
