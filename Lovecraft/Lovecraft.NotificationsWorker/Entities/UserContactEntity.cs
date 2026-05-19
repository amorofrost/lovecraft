using Azure;
using Azure.Data.Tables;

namespace Lovecraft.NotificationsWorker.Entities;

/// <summary>
/// Minimal projection of Lovecraft.Backend.Storage.Entities.UserEntity.
/// Reads from the 'users' table but only deserializes TelegramUserId, Email, and EmailVerified fields.
/// Used by the notifications worker for efficient contact method lookups.
/// Keep GetPartitionKey formula in sync with UserEntity.
/// </summary>
public class UserContactEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>Telegram user id (string) when linked via Login Widget / Mini App; empty otherwise.</summary>
    public string TelegramUserId { get; set; } = string.Empty;

    /// <summary>Email address when registered locally or attached; empty otherwise.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Whether the email has been verified via confirmation link.</summary>
    public bool EmailVerified { get; set; } = false;

    public static string GetPartitionKey(string userId) =>
        $"user-{userId[0].ToString().ToLower()}";
}
