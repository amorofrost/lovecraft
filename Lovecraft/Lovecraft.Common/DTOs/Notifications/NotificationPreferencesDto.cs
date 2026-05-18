using Lovecraft.Common.Enums;

namespace Lovecraft.Common.DTOs.Notifications;

public class NotificationPreferencesDto
{
    /// <summary>Per-type, per-channel toggle. Key = NotificationType camelCase. Inner key = NotificationChannel camelCase.</summary>
    public Dictionary<string, Dictionary<string, bool>> Matrix { get; set; } = new();
    /// <summary>Per-channel frequency. Key = NotificationChannel camelCase.</summary>
    public Dictionary<string, NotificationFrequency> Frequency { get; set; } = new();
    /// <summary>UTC hour (0-23) at which daily digests dispatch. Default 9.</summary>
    public int DailyDigestHourUtc { get; set; } = 9;
    /// <summary>Master kill switch.</summary>
    public bool Mute { get; set; }
    /// <summary>If set and in the future, all outbound channels are suppressed (canonical rows still written).</summary>
    public DateTime? MutedUntilUtc { get; set; }
}
