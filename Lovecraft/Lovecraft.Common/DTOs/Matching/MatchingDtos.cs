using Lovecraft.Common.Enums;

namespace Lovecraft.Common.DTOs.Matching;

public class LikeDto
{
    public string Id { get; set; } = string.Empty;
    public string FromUserId { get; set; } = string.Empty;
    public string ToUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsMatch { get; set; }
}

public class MatchDto
{
    public string Id { get; set; } = string.Empty;
    public List<string> Users { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public MessageDto? LastMessage { get; set; }
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

public class CreateLikeRequestDto
{
    public string ToUserId { get; set; } = string.Empty;
}

public class LikeResponseDto
{
    public LikeDto Like { get; set; } = new();
    public bool IsMatch { get; set; }
    public MatchDto? Match { get; set; }
}
