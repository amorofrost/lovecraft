using System.ComponentModel.DataAnnotations;

namespace Lovecraft.Common.DTOs.Notifications;

public class CreateBroadcastRequestDto
{
    [Required, MaxLength(100)]
    public string Title { get; set; } = "";

    [Required, MaxLength(1000)]
    public string Body { get; set; } = "";

    [MaxLength(500)]
    public string? Link { get; set; }

    [Required]
    public BroadcastAudienceDto Audience { get; set; } = new("all", null);
}
