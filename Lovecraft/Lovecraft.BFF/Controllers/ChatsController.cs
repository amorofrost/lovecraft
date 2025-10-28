using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lovecraft.BFF.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatsController : ControllerBase
{
    public record MessageDto(string Id, string ChatId, string SenderId, string Content, DateTime Timestamp, bool Read, string Type);

    public record ChatBase(string Id, string Type, string[] Participants, MessageDto? LastMessage, DateTime CreatedAt, DateTime UpdatedAt);
    public record PrivateChat(string Id, string Type, string[] Participants, MessageDto? LastMessage, DateTime CreatedAt, DateTime UpdatedAt, string MatchId, object OtherUser) : ChatBase(Id, Type, Participants, LastMessage, CreatedAt, UpdatedAt);
    public record GroupChat(string Id, string Type, string Name, string Description, string[] Participants, bool IsEventChat, string? EventId, string[] AdminIds, MessageDto? LastMessage, DateTime CreatedAt, DateTime UpdatedAt) : ChatBase(Id, Type, Participants, LastMessage, CreatedAt, UpdatedAt);

    private static readonly object[] PrivateChats = new object[]
    {
        new PrivateChat(
            Id: "private-1",
            Type: "private",
            Participants: new []{"current-user","1"},
            MatchId: "match-1",
            CreatedAt: new DateTime(2024,2,20),
            UpdatedAt: new DateTime(2024,2,22),
            LastMessage: new MessageDto("msg-1","private-1","1","Привет! Тоже обожаешь AloeVera?", new DateTime(2024,2,22,14,30,0), false, "text"),
            OtherUser: new {
                id = "1",
                name = "Анна",
                age = 25,
                bio = "Обожаю музыку AloeVera",
                location = "Москва",
                gender = "female",
                profileImage = "https://images.unsplash.com/photo-1494790108755-2616b612b786?w=400&h=600&fit=crop&crop=face",
                images = Array.Empty<string>(),
                lastSeen = DateTime.UtcNow,
                isOnline = true,
                preferences = new { ageRange = new[]{22,35}, maxDistance = 50, showMe = "everyone" },
                settings = new { profileVisibility = "public", anonymousLikes = false, language = "ru", notifications = true }
            }
        )
    };

    private static readonly GroupChat[] EventChats = new []
    {
        new GroupChat("event-1","group","Фан-встреча: Поэзия и музыка","Чат для участников встречи", new[]{"current-user","4","5","6","7"}, true, "2", new[]{"admin-1"}, new MessageDto("msg-3","event-1","1","Встречаемся у входа в 19:00!", new DateTime(2024,2,21,18,0,0), true, "text"), new DateTime(2024,2,18), new DateTime(2024,2,21)),
        new GroupChat("event-2","group","Концерт AloeVera - Москва","Общение участников концерта", new[]{"current-user","1","2","3"}, true, "1", new[]{"admin-1"}, new MessageDto("msg-4","event-2","2","Не могу дождаться концерта! 🎵", new DateTime(2024,2,22,12,30,0), true, "text"), new DateTime(2024,2,10), new DateTime(2024,2,22))
    };

    private static readonly GroupChat[] CommunityChats = new []
    {
        new GroupChat("community-1","group","📢 Официальные объявления","Новости и анонсы от команды", new[]{"current-user","1","2","3","4","5"}, false, null, new[]{"admin-1"}, new MessageDto("msg-5","community-1","admin-1","Новый альбом выходит в марте! 🎉", new DateTime(2024,2,23,10,0,0), false, "text"), new DateTime(2024,1,1), new DateTime(2024,2,23)),
        new GroupChat("community-2","group","💬 Общие темы","Обсуждение всего подряд", new[]{"current-user","1","2","3","4","5","6","7","8"}, false, null, new[]{"admin-1"}, new MessageDto("msg-6","community-2","3","Какая ваша любимая песня?", new DateTime(2024,2,23,9,15,0), true, "text"), new DateTime(2024,1,15), new DateTime(2024,2,23)),
        new GroupChat("community-3","group","🏙️ Москва","Чат для фанатов из Москвы", new[]{"current-user","1","2","5"}, false, null, new[]{"admin-1"}, new MessageDto("msg-7","community-3","1","Кто-нибудь в центре сегодня?", new DateTime(2024,2,22,16,45,0), true, "text"), new DateTime(2024,1,20), new DateTime(2024,2,22)),
        new GroupChat("community-4","group","🏙️ Санкт-Петербург","Чат для фанатов из Питера", new[]{"current-user","2","4","6"}, false, null, new[]{"admin-1"}, new MessageDto("msg-8","community-4","2","Планируете приехать на фестиваль?", new DateTime(2024,2,22,14,20,0), true, "text"), new DateTime(2024,1,20), new DateTime(2024,2,22)),
        new GroupChat("community-5","group","🎵 Музыкальные обсуждения","Разбор песен и творчества", new[]{"current-user","1","2","3","4","5","6"}, false, null, new[]{"admin-1"}, new MessageDto("msg-9","community-5","4","Текст последней песни просто космос 🌌", new DateTime(2024,2,23,11,30,0), true, "text"), new DateTime(2024,1,25), new DateTime(2024,2,23)),
        new GroupChat("community-6","group","🎨 Оффтопик","Обсуждение всего, кроме музыки", new[]{"current-user","1","3","5","7"}, false, null, new[]{"admin-1"}, new MessageDto("msg-10","community-6","5","Кто смотрел новый фильм?", new DateTime(2024,2,22,20,15,0), true, "text"), new DateTime(2024,2,1), new DateTime(2024,2,22))
    };

    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(new { privateChats = PrivateChats, eventChats = EventChats, communityChats = CommunityChats });
    }

    [Authorize]
    [HttpPost("send")]
    public IActionResult SendMessage([FromBody] dynamic payload)
    {
        // Accept and echo back in this mock
        return Ok(new { status = "sent", payload });
    }
}
