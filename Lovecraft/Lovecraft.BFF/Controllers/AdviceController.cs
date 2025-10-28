using Microsoft.AspNetCore.Mvc;

namespace Lovecraft.BFF.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdviceController : ControllerBase
{
    public record DailyItem(string Id, string Title, string Content, string Author, string Category, string Image);
    public record ArticleItem(string Id, string Title, string Content, string Author, string Category, string ReadTime, int Likes);
    public record ChallengeItem(string Id, string Title, string Description, string Reward, int Participants, bool Completed);
    public record AdvicePayload(IEnumerable<DailyItem> Daily, IEnumerable<ArticleItem> Articles, IEnumerable<ChallengeItem> Challenges);

    private static readonly AdvicePayload Payload = new(
        Daily: new []
        {
            new DailyItem("1","Совет дня от Веры","Будьте собой! Настоящая связь возникает только когда вы искренни. Не бойтесь показать свои увлечения и страсти.","Вера (AloeVera)","authenticity","https://images.unsplash.com/photo-1494790108755-2616b612b786?w=400&h=300&fit=crop&crop=face")
        },
        Articles: new []
        {
            new ArticleItem("2","Как начать разговор с новым человеком","Лучший способ начать беседу - найти общие интересы. Если вы оба фанаты AloeVera, начните с обсуждения любимых песен!","Психолог Анна Петрова","communication","5 мин",124),
            new ArticleItem("3","Музыка как язык любви","Музыкальные предпочтения многое говорят о человеке. Обсуждение любимых исполнителей может стать началом глубокой связи.","Музыкальный терапевт Дмитрий Волков","music","7 мин",89),
            new ArticleItem("4","Здоровые границы в отношениях","Важно помнить о своих потребностях и границах. Здоровые отношения строятся на взаимном уважении и понимании.","Семейный психолог Мария Иванова","relationships","10 мин",156)
        },
        Challenges: new []
        {
            new ChallengeItem("5","Вызов: Поделитесь плейлистом","Создайте плейлист из 5 песен AloeVera, которые описывают ваше настроение, и поделитесь с новым знакомством.","50 очков любви",342,false),
            new ChallengeItem("6","Неделя комплиментов","Делайте искренние комплименты всем своим совпадениям в течение недели. Посмотрите, как это изменит ваше общение!","100 очков любви",189,true)
        }
    );

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(Payload);
    }
}
