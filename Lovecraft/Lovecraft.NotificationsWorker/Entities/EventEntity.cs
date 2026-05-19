using Azure;
using Azure.Data.Tables;

namespace Lovecraft.NotificationsWorker.Entities;

/// <summary>
/// Worker-local partial mirror of <c>Lovecraft.Backend.Storage.Entities.EventEntity</c>.
/// Only the columns the <see cref="Services.EventReminderProcessor"/> needs are projected;
/// PartitionKey is the constant string <c>"EVENTS"</c>, RowKey is the eventId.
/// Keep PK/RK and field names in sync with the backend — schema drift causes silent runtime errors.
/// </summary>
public class EventEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "EVENTS";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Title { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Visibility { get; set; } = string.Empty;
    public bool Archived { get; set; }
}
