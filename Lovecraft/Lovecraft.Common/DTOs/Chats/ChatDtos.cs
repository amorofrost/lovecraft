using Lovecraft.Common.Enums;

namespace Lovecraft.Common.DTOs.Chats;

public class ChatDto
{
    public string Id { get; set; } = string.Empty;
    public ChatType Type { get; set; }
    public string? Name { get; set; }
    public List<string> Participants { get; set; } = new();
    public MessageDto? LastMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? EventId { get; set; }
}

public class MessageDto
{
    public string Id { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool Read { get; set; }
    public MessageType Type { get; set; } = MessageType.Text;
}

public class SendMessageRequestDto
{
    public string Content { get; set; } = string.Empty;
    public MessageType Type { get; set; } = MessageType.Text;
}

public class CreateChatRequestDto
{
    public ChatType Type { get; set; }
    public string? Name { get; set; }
    public List<string> ParticipantIds { get; set; } = new();
    public string? EventId { get; set; }
}
