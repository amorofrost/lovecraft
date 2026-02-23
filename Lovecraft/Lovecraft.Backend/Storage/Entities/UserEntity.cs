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
    public string PasswordHash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Bio { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string ProfileImage { get; set; } = string.Empty;
    public string ImagesJson { get; set; } = "[]";
    public bool IsOnline { get; set; }
    public DateTime LastSeen { get; set; }
    public bool EmailVerified { get; set; }
    public string AuthMethodsJson { get; set; } = "[]";
    public string PreferencesJson { get; set; } = "{}";
    public string SettingsJson { get; set; } = "{}";
    public string FavoriteSongJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static string GetPartitionKey(string userId) =>
        $"user-{userId[0].ToString().ToLower()}";
}
