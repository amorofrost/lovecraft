using Lovecraft.Common.Enums;

namespace Lovecraft.Common.DTOs.Admin;

public record AssignRoleRequestDto(StaffRole Role);
public record SetRankOverrideRequestDto(UserRank? Rank);
public record AppConfigDto(
    Dictionary<string, string> RankThresholds,
    Dictionary<string, string> Permissions,
    Dictionary<string, string> Registration);

public record CreateEventInviteRequestDto(DateTime ExpiresAtUtc);

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
