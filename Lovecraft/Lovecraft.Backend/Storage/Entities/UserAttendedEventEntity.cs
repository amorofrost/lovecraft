using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

/// <summary>
/// Reverse index of <see cref="EventAttendeeEntity"/>, partitioned by user so
/// "events attended by user X" is a single-partition scan instead of a
/// cross-partition RowKey-only filter.
///
/// PK = userId, RK = eventId. Dual-written on register/unregister/admin-remove.
/// Backfilled at backend startup from the canonical eventattendees table.
/// </summary>
public class UserAttendedEventEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public DateTime RegisteredAt { get; set; }
}
