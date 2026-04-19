using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class EventEntity : ITableEntity
{
    // PK = "EVENTS", RK = eventId
    public string PartitionKey { get; set; } = "EVENTS";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;

    public string BadgeImageUrl { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime? EndDate { get; set; }
    public string Location { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public string Category { get; set; } = string.Empty;

    /// <summary>Free-text price (currency as entered).</summary>
    public string Price { get; set; } = string.Empty;

    public string Organizer { get; set; } = string.Empty;

    /// <summary>Official event / ticket URL.</summary>
    public string ExternalUrl { get; set; } = string.Empty;

    public bool IsSecret { get; set; }

    /// <summary>Stored enum name: <c>Public</c>, <c>SecretHidden</c>, <c>SecretTeaser</c>. Empty falls back to <see cref="IsSecret"/>.</summary>
    public string Visibility { get; set; } = string.Empty;

    public string? ForumTopicId { get; set; }

    public bool Archived { get; set; }
}
