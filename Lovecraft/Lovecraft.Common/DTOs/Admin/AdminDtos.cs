using Lovecraft.Common.Enums;

namespace Lovecraft.Common.DTOs.Admin;

public record AssignRoleRequestDto(StaffRole Role);
public record SetRankOverrideRequestDto(UserRank? Rank);
public record AppConfigDto(
    Dictionary<string, string> RankThresholds,
    Dictionary<string, string> Permissions,
    Dictionary<string, string> Registration);
