using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class ChatEntity : ITableEntity
{
    // PartitionKey = "CHAT", RowKey = chatId
    public string PartitionKey { get; set; } = "CHAT";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string ParticipantIds { get; set; } = string.Empty; // comma-separated
    public DateTime CreatedAt { get; set; }
}
