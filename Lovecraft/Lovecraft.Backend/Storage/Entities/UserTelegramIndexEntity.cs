using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

/// <summary>PartitionKey = Telegram user id (string), RowKey = INDEX.</summary>
public class UserTelegramIndexEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = "INDEX";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string UserId { get; set; } = string.Empty;
}
