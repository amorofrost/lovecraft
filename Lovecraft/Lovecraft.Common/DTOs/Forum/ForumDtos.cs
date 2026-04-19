using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Lovecraft.Common.Enums;

namespace Lovecraft.Common.DTOs.Forum;

public class ForumSectionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TopicCount { get; set; }
    /// <summary>Display order among non-event sections (lower first).</summary>
    public int OrderIndex { get; set; }
    public string MinRank { get; set; } = "novice";
}

public class CreateForumSectionRequestDto
{
    [Required]
    [StringLength(64, MinimumLength = 2)]
    [RegularExpression(@"^[a-z][a-z0-9-]{1,62}$", ErrorMessage = "Id must be lowercase slug (a-z, digits, hyphen).")]
    public string Id { get; set; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>novice | activeMember | friendOfAloe | aloeCrew</summary>
    [Required]
    public string MinRank { get; set; } = "novice";
}

public class UpdateForumSectionRequestDto
{
    [StringLength(200, MinimumLength = 1)]
    public string? Name { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    /// <summary>novice | activeMember | friendOfAloe | aloeCrew</summary>
    public string? MinRank { get; set; }
}

public class ReorderForumSectionsRequestDto
{
    [Required]
    [MinLength(1)]
    public List<string> SectionIds { get; set; } = new();
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

    /// <summary>When <see cref="SectionId"/> is <c>events</c>, the owning event id.</summary>
    public string? EventId { get; set; }

    /// <summary>Access scope for event-linked topics; ignored for non-event sections.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EventTopicVisibility EventTopicVisibility { get; set; } = EventTopicVisibility.Public;

    /// <summary>When <see cref="EventTopicVisibility"/> is <see cref="EventTopicVisibility.SpecificUsers"/>.</summary>
    public List<string> AllowedUserIds { get; set; } = new();
}

/// <summary>One row per event for the Talks → event discussions tab (not a static forum section).</summary>
public class EventDiscussionSectionDto
{
    public string EventId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public EventVisibility Visibility { get; set; }
    public bool IsAttending { get; set; }
    public int TopicCount { get; set; }
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

    /// <summary>Up to 3 badge image URLs from events the author attended (newest first).</summary>
    public List<string> AuthorEventBadgeImageUrls { get; set; } = new();

    /// <summary>Total count of attended events that have a badge image (for +N overflow).</summary>
    public int AuthorEventBadgeTotalCount { get; set; }
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

    public EventTopicVisibility? EventTopicVisibility { get; set; }
    public List<string>? AllowedUserIds { get; set; }
}

public class CreateReplyRequestDto
{
    public string Content { get; set; } = string.Empty;
    public List<string>? ImageUrls { get; set; }
}

public class UpdateTopicRequestDto
{
    public string? Title { get; set; }
    public string? Content { get; set; }
    public bool? NoviceVisible { get; set; }
    public bool? NoviceCanReply { get; set; }
    public bool? IsPinned { get; set; }
    public bool? IsLocked { get; set; }

    public EventTopicVisibility? EventTopicVisibility { get; set; }
    /// <summary>When set, replaces the allow-list (use empty list to clear).</summary>
    public List<string>? AllowedUserIds { get; set; }
}
