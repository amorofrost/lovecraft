using System.ComponentModel.DataAnnotations;
using Lovecraft.Common.Enums;

namespace Lovecraft.Common.DTOs.Forum;

public class ForumSectionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TopicCount { get; set; }
    public string MinRank { get; set; } = "novice";
}

public class ForumTopicDto
{
    public string Id { get; set; } = string.Empty;
    public string SectionId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorAvatar { get; set; }
    public bool IsPinned { get; set; }
    public bool IsLocked { get; set; }
    public int ReplyCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string MinRank { get; set; } = "novice";
    public bool NoviceVisible { get; set; } = true;
    public bool NoviceCanReply { get; set; } = true;
}

public class ForumReplyDto
{
    public string Id { get; set; } = string.Empty;
    public string TopicId { get; set; } = string.Empty;
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorAvatar { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int Likes { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public UserRank AuthorRank { get; set; } = UserRank.Novice;
    public StaffRole AuthorStaffRole { get; set; } = StaffRole.None;
}

public class CreateTopicRequestDto
{
    [Required]
    [StringLength(100, MinimumLength = 5)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(5000, MinimumLength = 10)]
    public string Content { get; set; } = string.Empty;
    public bool? NoviceVisible { get; set; }
    public bool? NoviceCanReply { get; set; }
}

public class CreateReplyRequestDto
{
    public string Content { get; set; } = string.Empty;
    public List<string>? ImageUrls { get; set; }
}

public class UpdateTopicRequestDto
{
    public bool? NoviceVisible { get; set; }
    public bool? NoviceCanReply { get; set; }
    public bool? IsPinned { get; set; }
    public bool? IsLocked { get; set; }
}
