namespace Lovecraft.Common.DTOs.Notifications;

public class BroadcastDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Link { get; set; }
    public BroadcastAudienceDto Audience { get; set; } = new("all", null);
    public string IssuedByUserId { get; set; } = "";
    public DateTime IssuedAtUtc { get; set; }
    public int EstimatedRecipients { get; set; }
    public int DispatchedCount { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime? CompletedAtUtc { get; set; }
}
