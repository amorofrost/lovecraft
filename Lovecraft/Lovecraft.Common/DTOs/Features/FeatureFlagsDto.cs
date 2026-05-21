namespace Lovecraft.Common.DTOs.Features;

/// <summary>
/// Client-visible feature flag snapshot. Exposed by <c>GET /api/v1/features</c>
/// for the frontend to drive conditional UI (e.g. show/hide the Feed page).
/// </summary>
public class FeatureFlagsDto
{
    public bool FeedEnabled { get; set; }
}
