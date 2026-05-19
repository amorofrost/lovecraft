namespace Lovecraft.Common.DTOs.Notifications;

/// <summary>
/// One section of a digest email — all notifications of a single type.
/// </summary>
public class EmailDigestSectionDto
{
    /// <summary>Notification type (PascalCase enum name).</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>Human-readable section header, e.g. "New matches (1)".</summary>
    public string Header { get; set; } = string.Empty;
    /// <summary>Per-notification lines.</summary>
    public List<EmailDigestItemDto> Items { get; set; } = new();
}

public class EmailDigestItemDto
{
    public string Text { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
