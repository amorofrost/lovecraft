using Lovecraft.Backend.MockData;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Services;

public class MockNotificationPreferenceService : INotificationPreferenceService
{
    public Task<NotificationPreferencesDto> GetPreferencesAsync(string userId)
    {
        if (MockDataStore.NotificationPreferences.TryGetValue(userId, out var existing))
            return Task.FromResult(Clone(existing));

        return Task.FromResult(BuildDefaults());
    }

    public Task<NotificationPreferencesDto> UpdatePreferencesAsync(string userId, NotificationPreferencesDto prefs)
    {
        MockDataStore.NotificationPreferences[userId] = Clone(prefs);
        return Task.FromResult(Clone(prefs));
    }

    public static NotificationPreferencesDto BuildDefaults()
    {
        var prefs = new NotificationPreferencesDto { DailyDigestHourUtc = 9 };
        foreach (var name in Enum.GetNames<NotificationType>())
        {
            var key = char.ToLowerInvariant(name[0]) + name[1..];
            prefs.Matrix[key] = new Dictionary<string, bool>
            {
                { "inApp",    true  },
                { "telegram", false },
                { "webPush",  false },
                { "email",    false },
            };
        }
        prefs.Frequency["inApp"]    = NotificationFrequency.Immediate;
        prefs.Frequency["telegram"] = NotificationFrequency.Immediate;
        prefs.Frequency["webPush"]  = NotificationFrequency.Immediate;
        prefs.Frequency["email"]    = NotificationFrequency.Daily;
        return prefs;
    }

    private static NotificationPreferencesDto Clone(NotificationPreferencesDto src)
    {
        var copy = new NotificationPreferencesDto
        {
            DailyDigestHourUtc = src.DailyDigestHourUtc,
            Mute = src.Mute,
            MutedUntilUtc = src.MutedUntilUtc,
        };
        foreach (var kvp in src.Matrix)
            copy.Matrix[kvp.Key] = new Dictionary<string, bool>(kvp.Value);
        foreach (var kvp in src.Frequency)
            copy.Frequency[kvp.Key] = kvp.Value;
        return copy;
    }
}
