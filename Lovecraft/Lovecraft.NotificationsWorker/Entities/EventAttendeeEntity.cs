using Azure;
using Azure.Data.Tables;

namespace Lovecraft.NotificationsWorker.Entities;

/// <summary>
/// Worker-local partial mirror of <c>Lovecraft.Backend.Storage.Entities.EventAttendeeEntity</c>.
/// PartitionKey is the eventId, RowKey is the userId. <see cref="Services.EventReminderProcessor"/>
/// reads the RowKey as the recipient userId.
/// Keep in sync with the backend.
/// </summary>
public class EventAttendeeEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public DateTime RegisteredAt { get; set; }
}
