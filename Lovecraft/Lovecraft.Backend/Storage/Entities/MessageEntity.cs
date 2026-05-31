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
    // JSON-serialized Dictionary<string,string> mapping userId → emoji (one reaction per user).
    public string Reactions { get; set; } = "{}";
    // Empty string when this message is not a reply.
    public string ReplyToMessageId { get; set; } = string.Empty;
}
