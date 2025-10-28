using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lovecraft.BFF.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LikesController : ControllerBase
{
    public record UserLite(string Id, string Name, int Age, string Bio, string Location, string Gender, string ProfileImage, bool IsOnline);
    public record MatchItem(string Id, string[] Users, DateTime CreatedAt, bool IsRead, UserLite OtherUser);
    public record LikeItem(string Id, string FromUserId, string ToUserId, DateTime CreatedAt, bool IsMatch);
    public record SentLike(string Id, string FromUserId, string ToUserId, DateTime CreatedAt, bool IsMatch, UserLite ToUser);
    public record ReceivedLike(string Id, string FromUserId, string ToUserId, DateTime CreatedAt, bool IsMatch, bool IsRead, UserLite FromUser);

    private static readonly Dictionary<string, UserLite> Users = new()
    {
        ["1"] = new("1","Анна",25,"Обожаю музыку AloeVera","Москва","female","https://images.unsplash.com/photo-1494790108755-2616b612b786?w=400&h=600&fit=crop&crop=face", true),
        ["2"] = new("2","Дмитрий",28,"Музыкант, фанат AloeVera","Санкт-Петербург","male","https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=400&h=600&fit=crop&crop=face", false),
        ["3"] = new("3","Елена",22,"Танцую под AloeVera","Новосибирск","female","https://images.unsplash.com/photo-1438761681033-6461ffad8d80?w=400&h=600&fit=crop&crop=face", true),
        ["4"] = new("4","Мария",26,"Фотограф, люблю AloeVera","Москва","female","https://images.unsplash.com/photo-1517841905240-472988babdf9?w=400&h=600&fit=crop&crop=face", false),
        ["5"] = new("5","Алексей",30,"Музыкант AloeVera cover band","Екатеринбург","male","https://images.unsplash.com/photo-1472099645785-5658abf4ff4e?w=400&h=600&fit=crop&crop=face", false),
    };

    private static readonly List<MatchItem> Matches = new()
    {
        new("1", new[]{"current-user","1"}, new DateTime(2024,2,20), false, Users["1"]),
        new("4", new[]{"current-user","4"}, new DateTime(2024,2,22), true, Users["4"])
    };

    private static readonly List<SentLike> Sent = new()
    {
        new("2","current-user","2", new DateTime(2024,2,21), false, Users["2"])
    };

    private static readonly List<ReceivedLike> Received = new()
    {
        new("3","3","current-user", new DateTime(2024,2,19), false, false, Users["3"]),
        new("5","5","current-user", new DateTime(2024,2,18), false, true, Users["5"])
    };

    [HttpGet]
    public IActionResult GetLikes()
    {
        return Ok(new { matches = Matches, sent = Sent, received = Received });
    }

    [Authorize]
    [HttpPost]
    public IActionResult SendLike([FromBody] LikeItem like)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var toUser = Users.TryGetValue(like.ToUserId, out var u) ? u : null;
        if (toUser is not null)
        {
            Sent.Add(new SentLike(id, like.FromUserId, like.ToUserId, now, false, toUser));
        }
        return Ok(new { id, status = "ok" });
    }
}
