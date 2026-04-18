using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

/// <summary>
/// One row per issued invite code. <see cref="RowKey"/> is the normalized plaintext code (readable, case-insensitive lookup).
/// </summary>
public class EventInviteEntity : ITableEntity
{
    public const string PartitionValue = "INVITE";

    public string PartitionKey { get; set; } = PartitionValue;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>Event id for event invites, or a negative campaign id (e.g. <c>-1</c>) for non-event acquisition codes.</summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>Optional label for campaign rows (e.g. &quot;Summer 2026 ads&quot;).</summary>
    public string CampaignLabel { get; set; } = string.Empty;

    /// <summary>Same as <see cref="RowKey"/>; duplicated for readability in storage explorers.</summary>
    public string PlainCode { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
    public bool Revoked { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public int RegistrationCount { get; set; }
    public int EventAttendanceClaimCount { get; set; }
}
