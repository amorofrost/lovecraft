using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class UserChatEntity : ITableEntity
{
    // PartitionKey = userId, RowKey = chatId
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string OtherUserId { get; set; } = string.Empty;
    public string LastMessageContent { get; set; } = string.Empty;
    public DateTime LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}
