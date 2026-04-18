using Lovecraft.Common.Enums;

namespace Lovecraft.Common.DTOs.Admin;

public record AssignRoleRequestDto(StaffRole Role);
public record SetRankOverrideRequestDto(UserRank? Rank);
public record AppConfigDto(
    Dictionary<string, string> RankThresholds,
    Dictionary<string, string> Permissions,
    Dictionary<string, string> Registration);

/// <summary>Body for <c>POST /api/v1/admin/events/{eventId}/invites</c>.</summary>
public class CreateEventInviteRequestDto
{
    public DateTime ExpiresAtUtc { get; set; }
    /// <summary>If set, this plaintext is stored (normalized); otherwise a code is generated.</summary>
    public string? PlainCode { get; set; }
}

public record CreateEventInviteResponseDto(string PlainCode, DateTime ExpiresAtUtc);

public record ArchiveEventRequestDto(bool Archived);

/// <summary>Payload for creating or replacing an event (admin).</summary>
public class AdminEventWriteDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime? EndDate { get; set; }
    public string Location { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public EventCategory Category { get; set; }
    public decimal? Price { get; set; }
    public string Organizer { get; set; } = string.Empty;
    public EventVisibility Visibility { get; set; } = EventVisibility.Public;
    public bool Archived { get; set; }
}

public record EventAttendeeAdminDto(string UserId, string DisplayName);

public record EventInviteAdminDto(
    string PlainCode,
    string EventId,
    string? CampaignLabel,
    DateTime ExpiresAtUtc,
    bool Revoked,
    DateTime CreatedAtUtc,
    int RegistrationCount,
    int EventAttendanceClaimCount);

/// <summary>Creates a non-event invite tied to a negative campaign id (e.g. <c>-1</c>).</summary>
public class CreateCampaignInviteRequestDto
{
    /// <summary>Negative integer as string, e.g. <c>-1</c>, <c>-2</c>.</summary>
    public string CampaignId { get; set; } = string.Empty;
    public string? CampaignLabel { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    /// <summary>If omitted, a readable code is generated.</summary>
    public string? PlainCode { get; set; }
}
