using Lovecraft.Backend.Helpers;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Services.Notifications;

/// <summary>
/// Resolves a <see cref="BroadcastAudienceDto"/> into the list of recipient user IDs by
/// querying <see cref="IUserService"/> / <see cref="IEventService"/>. Supports four audience
/// types: "all", "attendingEvent", "minRank", "staffRole". Unknown types yield an empty list.
/// </summary>
public class BroadcastAudienceResolver : IBroadcastAudienceResolver
{
    // Paged bulk-list "take" — large enough to capture every active user in the foreseeable
    // future of the platform (audience-resolver runs at broadcast-create time, so a single
    // round-trip is acceptable). When the user table grows past this, switch to paginated.
    private const int BulkPageSize = 10_000;

    private readonly IUserService _users;
    private readonly IEventService _events;

    public BroadcastAudienceResolver(IUserService users, IEventService events)
    {
        _users = users;
        _events = events;
    }

    public async Task<IReadOnlyList<string>> ResolveAsync(BroadcastAudienceDto audience, CancellationToken ct)
    {
        if (audience is null) return Array.Empty<string>();

        switch (audience.Type)
        {
            case "all":
            {
                var all = await _users.GetUsersAsync(skip: 0, take: BulkPageSize);
                return all.Select(u => u.Id).Distinct().ToList();
            }

            case "attendingEvent":
            {
                if (string.IsNullOrWhiteSpace(audience.Value)) return Array.Empty<string>();
                var attendees = await _events.GetEventAttendeesAsync(audience.Value);
                return attendees.Select(a => a.UserId).Distinct().ToList();
            }

            case "minRank":
            {
                if (string.IsNullOrWhiteSpace(audience.Value)) return Array.Empty<string>();
                var minLevel = EffectiveLevel.Parse(audience.Value);
                var all = await _users.GetUsersAsync(skip: 0, take: BulkPageSize);
                return all
                    .Where(u => LevelOf(u) >= minLevel)
                    .Select(u => u.Id)
                    .Distinct()
                    .ToList();
            }

            case "staffRole":
            {
                if (string.IsNullOrWhiteSpace(audience.Value)) return Array.Empty<string>();
                if (!TryParseStaffRole(audience.Value, out var targetRole))
                    return Array.Empty<string>();
                var all = await _users.GetUsersAsync(skip: 0, take: BulkPageSize);
                return all
                    .Where(u => u.StaffRole == targetRole)
                    .Select(u => u.Id)
                    .Distinct()
                    .ToList();
            }

            default:
                return Array.Empty<string>();
        }
    }

    private static int LevelOf(UserDto user)
    {
        var rankLevel = (int)user.Rank;
        var staffLevel = user.StaffRole switch
        {
            StaffRole.Admin => EffectiveLevel.Admin,
            StaffRole.Moderator => EffectiveLevel.Moderator,
            _ => 0,
        };
        return Math.Max(rankLevel, staffLevel);
    }

    private static bool TryParseStaffRole(string value, out StaffRole role)
    {
        switch (value.ToLowerInvariant())
        {
            case "none": role = StaffRole.None; return true;
            case "moderator": role = StaffRole.Moderator; return true;
            case "admin": role = StaffRole.Admin; return true;
            default: role = StaffRole.None; return false;
        }
    }
}
