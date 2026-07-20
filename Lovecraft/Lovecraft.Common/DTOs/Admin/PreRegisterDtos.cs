namespace Lovecraft.Common.DTOs.Admin;

/// <summary>One imported attendee. Only the Telegram username and name are required.</summary>
public class PreRegisterAttendeeDto
{
    public string TelegramUsername { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public string? PhotoUrl { get; set; }
}

public class PreRegisterAttendeesRequestDto
{
    public List<PreRegisterAttendeeDto> Attendees { get; set; } = new();
}

/// <summary>Per-row outcome. Status is one of:
/// "created" | "skippedExists" | "invalidUsername" | "invalidName" | "error".</summary>
public class PreRegisterRowResultDto
{
    public string TelegramUsername { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Message { get; set; }
}

public class PreRegisterSummaryDto
{
    public int Created { get; set; }
    public int SkippedExists { get; set; }
    public int InvalidUsername { get; set; }
    public int InvalidName { get; set; }
    public int Error { get; set; }
}

public class PreRegisterResultDto
{
    public PreRegisterSummaryDto Summary { get; set; } = new();
    public List<PreRegisterRowResultDto> Results { get; set; } = new();
}
