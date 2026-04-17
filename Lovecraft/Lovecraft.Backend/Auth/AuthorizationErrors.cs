namespace Lovecraft.Backend.Auth;

public static class AuthorizationErrors
{
    public const string InsufficientRank = "INSUFFICIENT_RANK";
    public const string ModeratorRequired = "MODERATOR_REQUIRED";
    public const string AdminRequired = "ADMIN_REQUIRED";

    public const string InsufficientRankMessage = "Insufficient rank for this action";
    public const string ModeratorRequiredMessage = "Moderator or Admin role required";
    public const string AdminRequiredMessage = "Admin role required";
}
