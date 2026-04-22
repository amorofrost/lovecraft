using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class AuthTokenEntity : ITableEntity
{
    // PK = token GUID, RK = "VERIFY" | "RESET" | "ATTACH"
    // ATTACH rows carry a pending email+password swap for Telegram-only accounts; only
    // applied to the UserEntity when the user clicks the verification link.
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; }

    /// <summary>ATTACH only: PBKDF2 hash of the password the user chose. Applied on confirm.</summary>
    public string PendingPasswordHash { get; set; } = string.Empty;
}
