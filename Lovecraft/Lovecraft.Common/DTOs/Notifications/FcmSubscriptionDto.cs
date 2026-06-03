namespace Lovecraft.Common.DTOs.Notifications;

/// <summary>A registered FCM device token for a user (one per app install).</summary>
public class FcmSubscriptionDto
{
    public string DeviceId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public string DeviceModel { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
}

public class FcmRegisterRequestDto
{
    /// <summary>Stable per-install id; generated server-side if omitted.</summary>
    public string? DeviceId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public string DeviceModel { get; set; } = string.Empty;
}
