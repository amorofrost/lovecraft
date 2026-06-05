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
    /// <summary>UTC time the message was last edited, or null if it was never edited.</summary>
    public DateTime? EditedAt { get; set; }
    public bool Read { get; set; }
    public MessageType Type { get; set; } = MessageType.Text;
    public List<string> ImageUrls { get; set; } = new();
    /// <summary>userId → emoji. One reaction per user per message; senders cannot react to their own messages.</summary>
    public Dictionary<string, string> Reactions { get; set; } = new();
    /// <summary>Id of the message this is a reply to, or null. Always references a message in the same chat.</summary>
    public string? ReplyToMessageId { get; set; }
    /// <summary>Snapshot of the replied-to message for UI rendering. Populated server-side when ReplyToMessageId is set.</summary>
    public MessageReplySnippetDto? ReplyToSnippet { get; set; }
}

/// <summary>
/// Compact snapshot of a referenced (replied-to) message — embedded on the replying
/// message so clients can render the quote even when the original is in an unloaded
/// history page.
/// </summary>
public class MessageReplySnippetDto
{
    public string Id { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    /// <summary>First ~100 chars of the original Content (no BB code stripping).</summary>
    public string ContentPreview { get; set; } = string.Empty;
    public bool HasImages { get; set; }
}

public class SendMessageRequestDto
{
    public string Content { get; set; } = string.Empty;
    public MessageType Type { get; set; } = MessageType.Text;
    public List<string>? ImageUrls { get; set; }
    /// <summary>Optional id of a message in the same chat that this message replies to.</summary>
    public string? ReplyToMessageId { get; set; }
}

public class CreateChatRequestDto
{
    public ChatType Type { get; set; }
    public string? Name { get; set; }
    public List<string> ParticipantIds { get; set; } = new();
    public string? EventId { get; set; }
}

public class CreatePrivateChatRequestDto
{
    public string TargetUserId { get; set; } = string.Empty;
}

public class SetReactionRequestDto
{
    public string Emoji { get; set; } = string.Empty;
}

public class EditMessageRequestDto
{
    public string Content { get; set; } = string.Empty;
}
