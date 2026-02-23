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
    public DateTime Date { get; set; }
    public DateTime? EndDate { get; set; }
    public string Location { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public string Category { get; set; } = string.Empty;
    public double? Price { get; set; }
    public string Organizer { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
}
