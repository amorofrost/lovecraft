using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class MessageEntity : ITableEntity
{
    // PartitionKey = chatId, RowKey = {invertedTicks}_{messageId}
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string MessageId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public string Type { get; set; } = "text";
    public bool Read { get; set; }
    public string ImageUrls { get; set; } = "[]"; // stored as JSON array
}
