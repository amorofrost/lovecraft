using Microsoft.AspNetCore.Mvc;

namespace Lovecraft.BFF.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private static readonly UsersController.EventDto[] Upcoming = new[]
    {
        new UsersController.EventDto("1","Концерт AloeVera: Новые горизонты",
            "Эксклюзивный концерт с новыми песнями и встречей с фанатами. Приходите знакомиться под любимую музыку!",
            "https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?w=800&h=400&fit=crop",
            new DateTime(2024,12,15,19,0,0), new DateTime(2024,12,15,23,0,0),
            "Театр \"Мир\", Москва", 500, new List<string>{"1","2","3"}, "concert", 2500, "AloeVera Official", null),
        new UsersController.EventDto("2","Фан-встреча: Поэзия и музыка",
            "Неформальная встреча фанатов для обсуждения творчества группы и знакомств. Приносите гитары!",
            "https://images.unsplash.com/photo-1516450360452-9312f5e86fc7?w=800&h=400&fit=crop",
            new DateTime(2024,11,8,15,0,0), new DateTime(2024,11,8,18,0,0),
            "Парк Сокольники, Москва", null, new List<string>{"4","5","6","7"}, "meetup", null, "Фан-клуб AloeVera", null),
        new UsersController.EventDto("3","AloeVera Fest 2024",
            "Большой фестиваль с участием группы и приглашенных артистов. Два дня музыки, любви и знакомств!",
            "https://images.unsplash.com/photo-1459749411175-04bf5292ceea?w=800&h=400&fit=crop",
            new DateTime(2025,6,20,12,0,0), new DateTime(2025,6,21,23,0,0),
            "Лужники, Москва", 50000, new List<string>{"8","9","10","11","12"}, "festival", 5000, "AloeVera Official", null),
        new UsersController.EventDto("9","Яхтинг в Автралии 2026",
            "Олег. Австралия. Только для тех, кто знает.",
            "https://images.unsplash.com/photo-1544551763-46a013bb70d5?w=800&h=400&fit=crop",
            new DateTime(2026,4,15,10,0,0), new DateTime(2026,4,22,18,0,0),
            "Золотое побережье, Австралия", 50, new List<string>{"1","13","14","15"}, "yachting", 25000, "Veter Veter", true)
    };

    private static readonly UsersController.EventDto[] Past = new[]
    {
        new UsersController.EventDto("4","AloeVera Summer Tour 2023",
            "Летний тур группы AloeVera по России. Незабываемые концерты под открытым небом!",
            "https://images.unsplash.com/photo-1501386761578-eac5c94b800a?w=800&h=400&fit=crop",
            new DateTime(2023,8,15,20,0,0), new DateTime(2023,8,15,23,30,0),
            "Гребной канал, Санкт-Петербург", 15000, new List<string>{"1","5","6","7","8"}, "concert", 3000, "AloeVera Official", null),
        new UsersController.EventDto("5","Акустический вечер: Близко к сердцу",
            "Камерный концерт в уютной атмосфере. Только живая музыка и душевные разговоры.",
            "https://images.unsplash.com/photo-1540039155733-5bb30b53aa14?w=800&h=400&fit=crop",
            new DateTime(2023,10,12,19,30,0), new DateTime(2023,10,12,22,0,0),
            "Клуб \"Вечность\", Москва", 200, new List<string>{"1","2","3"}, "concert", 1500, "AloeVera Official", null),
        new UsersController.EventDto("6","Новогодний бал фанатов",
            "Праздничная встреча фанатов группы с конкурсами, подарками и сюрпризами от AloeVera.",
            "https://images.unsplash.com/photo-1482575832494-771f77fd8ba2?w=800&h=400&fit=crop",
            new DateTime(2022,12,30,21,0,0), new DateTime(2023,1,1,2,0,0),
            "Дворец культуры, Москва", 800, new List<string>{"1","2","3","4"}, "party", 2000, "Фан-клуб AloeVera", null),
        new UsersController.EventDto("7","AloeVera Fest 2022",
            "Первый большой фестиваль группы с участием звёздных гостей и множеством активностей.",
            "https://images.unsplash.com/photo-1533174072545-7a4b6ad7a6c3?w=800&h=400&fit=crop",
            new DateTime(2022,7,15,14,0,0), new DateTime(2022,7,16,23,0,0),
            "Парк Горького, Москва", 25000, new List<string>{"1","4","5","6","7"}, "festival", 4000, "AloeVera Official", null),
        new UsersController.EventDto("8","Винтажный вечер: Ретро-хиты",
            "Вечер старых хитов группы в стиле ретро. Потанцуем под любимые песни прошлых лет!",
            "https://images.unsplash.com/photo-1514525253161-7a46d19cd819?w=800&h=400&fit=crop",
            new DateTime(2023,3,25,20,0,0), new DateTime(2023,3,25,23,30,0),
            "Клуб \"Джаз\", Санкт-Петербург", 300, new List<string>{"1","2","3"}, "party", 1800, "Фан-клуб AloeVera", null),
        new UsersController.EventDto("10","АлоэЯхтинг 2025",
            "Юбилейный пятый яхтинг в Греции!",
            "https://images.unsplash.com/photo-1439066615861-d1af74d74000?w=800&h=400&fit=crop",
            new DateTime(2025,8,10,9,0,0), new DateTime(2025,8,17,19,0,0),
            "Кос, Греция", 40, new List<string>{"1","2","3","4","5"}, "yachting", 22000, "Mediterranean Sailing", null)
    };

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { upcoming = Upcoming, past = Past });
    }

    [HttpGet("{id}")]
    public IActionResult GetById(string id)
    {
        var evt = Upcoming.Concat(Past).FirstOrDefault(e => e.Id == id);
        if (evt is null) return NotFound();
        return Ok(evt);
    }
}
