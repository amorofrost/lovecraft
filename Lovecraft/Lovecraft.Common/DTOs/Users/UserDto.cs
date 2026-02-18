using Lovecraft.Common.Enums;

namespace Lovecraft.Common.DTOs.Users;

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Bio { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public Gender Gender { get; set; }
    public string ProfileImage { get; set; } = string.Empty;
    public List<string> Images { get; set; } = new();
    public DateTime LastSeen { get; set; }
    public bool IsOnline { get; set; }
    public List<string>? EventsAttended { get; set; }
    public AloeVeraSongDto? FavoriteSong { get; set; }
    public UserPreferencesDto Preferences { get; set; } = new();
    public UserSettingsDto Settings { get; set; } = new();
}

public class UserPreferencesDto
{
    public int AgeRangeMin { get; set; } = 18;
    public int AgeRangeMax { get; set; } = 65;
    public int MaxDistance { get; set; } = 50;
    public ShowMePreference ShowMe { get; set; } = ShowMePreference.Everyone;
}

public class UserSettingsDto
{
    public ProfileVisibility ProfileVisibility { get; set; } = ProfileVisibility.Public;
    public bool AnonymousLikes { get; set; }
    public Language Language { get; set; } = Language.Ru;
    public bool Notifications { get; set; } = true;
}

public class AloeVeraSongDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string PreviewUrl { get; set; } = string.Empty;
    public int Year { get; set; }
}
