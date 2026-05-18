using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Services.Notifications;

/// <summary>Snapshot of which channels a user has set up (link / verify / subscribe).</summary>
public class ChannelAvailability
{
    public bool TelegramLinked { get; set; }
    public bool EmailVerified { get; set; }
    public bool WebPushSubscribed { get; set; }
}

/// <summary>
/// Pure function: given user prefs, the notification type, and which channels the user
/// has set up, return the list of channels to write outbox rows for. In-app is the
/// inbox baseline — it stays on by default even when the matrix key is missing.
/// </summary>
public static class NotificationPolicy
{
    public static List<NotificationChannel> ResolveChannels(
        NotificationPreferencesDto prefs,
        NotificationType type,
        ChannelAvailability avail)
    {
        if (prefs.Mute) return new();
        if (prefs.MutedUntilUtc.HasValue && prefs.MutedUntilUtc.Value > DateTime.UtcNow) return new();

        var key = ChannelKey(type);
        var row = prefs.Matrix.TryGetValue(key, out var r) ? r : new Dictionary<string, bool>();

        var result = new List<NotificationChannel>();

        if (Enabled(row, "inApp", defaultValue: true))
            result.Add(NotificationChannel.InApp);

        if (Enabled(row, "telegram") && avail.TelegramLinked)
            result.Add(NotificationChannel.Telegram);

        if (Enabled(row, "webPush") && avail.WebPushSubscribed)
            result.Add(NotificationChannel.WebPush);

        if (Enabled(row, "email") && avail.EmailVerified)
            result.Add(NotificationChannel.Email);

        return result;
    }

    private static bool Enabled(Dictionary<string, bool> row, string channelKey, bool defaultValue = false)
        => row.TryGetValue(channelKey, out var v) ? v : defaultValue;

    private static string ChannelKey(NotificationType type)
    {
        var name = type.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
