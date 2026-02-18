using Lovecraft.Common.Enums;

namespace Lovecraft.Common.DTOs.Events;

public class EventDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime? EndDate { get; set; }
    public string Location { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public List<string> Attendees { get; set; } = new();
    public EventCategory Category { get; set; }
    public decimal? Price { get; set; }
    public string Organizer { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
}

public class EventRegistrationRequestDto
{
    public string EventId { get; set; } = string.Empty;
}
