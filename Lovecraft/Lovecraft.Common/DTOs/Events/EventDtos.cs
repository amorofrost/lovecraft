using Lovecraft.Common.Enums;

namespace Lovecraft.Common.DTOs.Events;

public class EventDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>Small badge image URL for profiles and forum (optional).</summary>
    public string BadgeImageUrl { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime? EndDate { get; set; }
    public string Location { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public List<string> Attendees { get; set; } = new();

    /// <summary>Users who clicked &quot;interested&quot; (not yet attendees).</summary>
    public List<string> InterestedUserIds { get; set; } = new();

    public EventCategory Category { get; set; }

    /// <summary>Free-text price (e.g. &quot;2500 ₽&quot;, &quot;from $100&quot;).</summary>
    public string Price { get; set; } = string.Empty;

    public string Organizer { get; set; } = string.Empty;

    /// <summary>Official event page or ticket purchase URL (optional).</summary>
    public string ExternalUrl { get; set; } = string.Empty;

    /// <summary>Legacy clients: true when <see cref="Visibility"/> is not <see cref="EventVisibility.Public"/>.</summary>
    public bool IsSecret { get; set; }

    public EventVisibility Visibility { get; set; } = EventVisibility.Public;

    public string? ForumTopicId { get; set; }

    /// <summary>When true, the event is hidden from public listings and detail APIs (admin can still manage it).</summary>
    public bool Archived { get; set; }
}

public class EventRegistrationRequestDto
{
    public string EventId { get; set; } = string.Empty;
}

/// <summary>
/// Response shape for <c>GET /api/v1/invites/{code}/event</c> — returns the event the
/// invite is tied to. Used by the Telegram Mini App to route a signed-in user who arrives
/// via an invite deep link (<c>t.me/{bot}?startapp=inv-{code}</c>) to the matching event
/// detail page. Returns 404 for unknown / revoked / expired codes and for campaign codes
/// (negative event ids), which don't map to a real event.
/// </summary>
public class InviteEventLookupDto
{
    public string EventId { get; set; } = string.Empty;
}

/// <summary>Optional body for <c>POST /api/v1/events/{id}/register</c> when attributing attendance to an invite code.</summary>
public class RegisterForEventRequestDto
{
    /// <summary>Must match this event when used; increments the invite&apos;s attendance-claim counter.</summary>
    public string? InviteCode { get; set; }
}
