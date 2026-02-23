using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class UserEmailIndexEntity : ITableEntity
{
    // PK = email.ToLower(), RK = "INDEX"
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = "INDEX";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string UserId { get; set; } = string.Empty;
}
