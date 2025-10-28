using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lovecraft.BFF.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    // Types mirrored from frontend
    public record AloeVeraSong(string Id, string Title, string Album, string Duration, string PreviewUrl, int Year);
    public record EventDto(string Id, string Title, string Description, string ImageUrl, DateTime Date, DateTime? EndDate,
        string Location, int? Capacity, List<string> Attendees, string Category, decimal? Price, string Organizer, bool? IsSecret);

    public record UserPreferences((int, int) AgeRange, int MaxDistance, string ShowMe);
    public record UserSettings(string ProfileVisibility, bool AnonymousLikes, string Language, bool Notifications);

    public record UserDto(
        string Id,
        string Name,
        int Age,
        string Bio,
        string Location,
        string Gender,
        string ProfileImage,
        List<string> Images,
        DateTime LastSeen,
        bool IsOnline,
        List<EventDto>? EventsAttended,
        AloeVeraSong? FavoriteSong,
        UserPreferences Preferences,
        UserSettings Settings
    );

    // Mock songs/events/users data copied from TS shapes
    private static readonly List<AloeVeraSong> Songs = new()
    {
        new("1","Звездное небо","Первый альбом","3:45","https://www.soundjay.com/misc/sounds/bell-ringing-05.wav",2018),
        new("2","Летний ветер","Первый альбом","4:12","https://www.soundjay.com/misc/sounds/bell-ringing-05.wav",2018),
        new("3","Новые горизонты","Второй альбом","3:28","https://www.soundjay.com/misc/sounds/bell-ringing-05.wav",2020),
        new("4","В объятиях тишины","Второй альбом","4:55","https://www.soundjay.com/misc/sounds/bell-ringing-05.wav",2020),
        new("5","Дыхание города","Третий альбом","3:33","https://www.soundjay.com/misc/sounds/bell-ringing-05.wav",2022)
    };

    private static readonly List<EventDto> Events = new()
    {
        new("1","AloeVera: Новые Горизонты","Большой концерт в поддержку нового альбома",
            "https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?w=800&h=600",
            new DateTime(2023,6,15), null, "Крокус Сити Холл, Москва", 7000, new List<string>{"1","2"}, "concert", 3500, "AloeVera Official", null),
        new("2","Акустический вечер: Близко к сердцу","Камерное выступление с акустическими версиями любимых песен",
            "https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?w=800&h=600",
            new DateTime(2024,3,20), null, "Театр Эстрады, Санкт-Петербург", 500, new List<string>{"1","3"}, "concert", 2500, "AloeVera Official", null),
        new("3","AloeVera Summer Fest","Летний фестиваль под открытым небом",
            "https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?w=800&h=600",
            new DateTime(2024,7,10), null, "Парк Сокольники, Москва", 10000, new List<string>{"2","3","4"}, "festival", 4000, "AloeVera Official", null),
        new("4","Фан-встреча: Музыка и Общение","Неформальная встреча поклонников группы",
            "https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?w=800&h=600",
            new DateTime(2024,5,12), null, "Клуб Plan B, Москва", 200, new List<string>{"1","4","5"}, "meetup", null, "AloeVera Fan Club", null),
        new("5","Новогодний концерт 2024","Празднование Нового года с любимой группой",
            "https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?w=800&h=600",
            new DateTime(2023,12,31), null, "Олимпийский, Москва", 15000, new List<string>{"5","6","7"}, "party", 5000, "AloeVera Official", null),
        new("6","Винтажный вечер: Ретро-хиты","Вечер старых хитов группы в камерной обстановке",
            "https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?w=800&h=600",
            new DateTime(2024,2,14), null, "Клуб Космонавт, Санкт-Петербург", 300, new List<string>{"2","6","7"}, "party", 2000, "AloeVera Official", null)
    };

    private static readonly List<UserDto> Users = new()
    {
        new("1","Анна",25,
            "Обожаю музыку AloeVera и концерты под открытым небом. Ищу того, с кем можно петь любимые песни ❤️",
            "Москва","female",
            "https://images.unsplash.com/photo-1438761681033-6461ffad8d80?w=400&h=600&fit=crop&crop=face",
            new List<string>(), DateTime.UtcNow, true,
            new List<EventDto>{Events[0]}, Songs[0],
            new UserPreferences((22,35), 50, "everyone"), new UserSettings("public", false, "ru", true)
        ),
        new("2","Дмитрий",28,
            "Музыкант, фанат AloeVera с первого альбома. Играю на гитаре и пишу песни. Давайте создадим дуэт! 🎸",
            "Санкт-Петербург","male",
            "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=400&h=600&fit=crop&crop=face",
            new List<string>(), DateTime.UtcNow, false,
            new List<EventDto>(), null,
            new UserPreferences((22,35), 50, "everyone"), new UserSettings("public", false, "ru", true)
        ),
        new("3","Елена",22,
            "Танцую под AloeVera, хожу на все концерты. Ищу романтика, который разделит мою страсть к музыке 💃",
            "Новосибирск","female",
            "https://images.unsplash.com/photo-1438761681033-6461ffad8d80?w=400&h=600&fit=crop&crop=face",
            new List<string>(), DateTime.UtcNow, true,
            new List<EventDto>{Events[1], Events[2], Events[4], Events[5], Events[0], Events[3]}, Songs[2],
            new UserPreferences((22,35), 50, "everyone"), new UserSettings("public", false, "ru", true)
        ),
        new("4","Мария",23,
            "Поэтесса и меломан. AloeVera вдохновляет меня на стихи",
            "Москва","female",
            "https://images.unsplash.com/photo-1438761681033-6461ffad8d80?w=400&h=600&fit=crop&crop=face",
            new List<string>(), DateTime.UtcNow, true,
            new List<EventDto>{Events[2], Events[3]}, null,
            new UserPreferences((22,35), 50, "everyone"), new UserSettings("public", false, "ru", true)
        ),
        new("5","Александр",26,
            "Фотограф и фанат AloeVera. Ищу музу и вторую половинку",
            "Москва","male",
            "https://images.unsplash.com/photo-1472099645785-5658abf4ff4e?w=400&h=600&fit=crop&crop=face",
            new List<string>(), DateTime.UtcNow, false,
            new List<EventDto>{Events[3], Events[4]}, null,
            new UserPreferences((22,35), 50, "everyone"), new UserSettings("public", false, "ru", true)
        ),
        new("6","София",24,
            "Художница, рисую под музыку AloeVera. Творческая душа ищет понимание",
            "Москва","female",
            "https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=400&h=600&fit=crop&crop=face",
            new List<string>(), DateTime.UtcNow, true,
            new List<EventDto>{Events[4], Events[5]}, null,
            new UserPreferences((22,35), 50, "everyone"), new UserSettings("public", false, "ru", true)
        ),
        new("7","Максим",27,
            "Барабанщик, мечтаю сыграть на одной сцене с AloeVera",
            "Москва","male",
            "https://images.unsplash.com/photo-1500648767791-00dcc994a43e?w=400&h=600&fit=crop&crop=face",
            new List<string>(), DateTime.UtcNow, false,
            new List<EventDto>{Events[4], Events[5]}, null,
            new UserPreferences((22,35), 50, "everyone"), new UserSettings("public", false, "ru", true)
        )
    };

    [HttpGet]
    public ActionResult<IEnumerable<UserDto>> GetUsers()
    {
        return Ok(Users);
    }

    [HttpGet("{id}")]
    public ActionResult<UserDto> GetUser(string id)
    {
        var user = Users.FirstOrDefault(u => u.Id == id);
        if (user is null) return NotFound();
        return Ok(user);
    }
}
