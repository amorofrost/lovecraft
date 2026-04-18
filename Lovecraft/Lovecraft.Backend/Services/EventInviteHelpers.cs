using System.Globalization;

namespace Lovecraft.Backend.Services;

public static class EventInviteHelpers
{
    /// <summary>
    /// Campaign / acquisition invites use a negative integer <see cref="EventId"/> (e.g. <c>-1</c>, <c>-2</c>).
    /// These codes can be used for registration but do not map to a real event row.
    /// </summary>
    public static bool IsCampaignEventId(string eventId)
    {
        if (string.IsNullOrEmpty(eventId))
            return false;
        if (eventId[0] != '-')
            return false;
        return int.TryParse(eventId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n < 0;
    }
}
