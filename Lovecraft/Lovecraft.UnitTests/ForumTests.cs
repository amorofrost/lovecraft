using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.Enums;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Lovecraft.UnitTests;

/// <summary>
/// Tests for MockForumService covering topic creation, section topic count
/// increment, unknown-section error handling, pinned-first ordering, and
/// reply count increment. Each test gets a clean MockDataStore slate via
/// constructor/Dispose.
/// </summary>
[Collection("ForumTests")]
public class ForumTests : IDisposable
{
    private static MockForumService CreateService()
    {
        var users = new MockUserService(new MockAppConfigService());
        return new MockForumService(users, new MockEventService(users));
    }

    private const string SectionId = "general";

    public ForumTests()
    {
        MockDataStore.ForumTopics.Clear();
        MockDataStore.ForumSections.Clear();
        MockDataStore.ForumReplies.Clear();
        Seed();
    }

    public void Dispose()
    {
        MockDataStore.ForumTopics.Clear();
        MockDataStore.ForumSections.Clear();
        MockDataStore.ForumReplies.Clear();
        Seed();
    }

    /// <summary>
    /// Mirror of the MockDataStore static initializer for forum data.
    /// Ensures at least one IsPinned=true topic and one IsPinned=false topic exist
    /// in SectionId's section ("general") so GetTopics_ReturnsPinnedFirst works.
    /// </summary>
    private static void Seed()
    {
        MockDataStore.ForumSections.AddRange(new[]
        {
            new ForumSectionDto { Id = "general",  Name = "💬 Общие обсуждения",     Description = "Свободное общение на любые темы",          TopicCount = 4 },
            new ForumSectionDto { Id = "music",    Name = "🎵 Музыка и творчество",   Description = "Разбор песен, каверы, творчество",          TopicCount = 3 },
            new ForumSectionDto { Id = "cities",   Name = "🏙️ По городам",            Description = "Общение по городам и регионам",             TopicCount = 3 },
            new ForumSectionDto { Id = "offtopic", Name = "🎨 Оффтопик",              Description = "Всё, что не связано с музыкой",             TopicCount = 2 },
        });

        MockDataStore.ForumTopics.AddRange(new[]
        {
            // general — two pinned, two unpinned
            new ForumTopicDto { Id = "t1",  SectionId = "general",  Title = "Какая ваша любимая песня AloeVera?",   Content = "Делитесь любимыми треками!",                   AuthorId = "1", AuthorName = "Анна",      IsPinned = true,  ReplyCount = 24, CreatedAt = new DateTime(2024, 2, 20),       UpdatedAt = new DateTime(2024, 2, 23, 9, 15, 0) },
            new ForumTopicDto { Id = "t2",  SectionId = "general",  Title = "Новый альбом — ваши впечатления",      Content = "Обсуждаем новый альбом группы",                 AuthorId = "2", AuthorName = "Дмитрий",   IsPinned = true,  ReplyCount = 42, CreatedAt = new DateTime(2024, 2, 21),       UpdatedAt = new DateTime(2024, 2, 23, 11, 30, 0) },
            new ForumTopicDto { Id = "t3",  SectionId = "general",  Title = "Кто едет на летний фестиваль?",        Content = "Планируем поездку вместе",                      AuthorId = "3", AuthorName = "Елена",     IsPinned = false, ReplyCount = 18, CreatedAt = new DateTime(2024, 2, 20),       UpdatedAt = new DateTime(2024, 2, 22, 16, 45, 0) },
            new ForumTopicDto { Id = "t4",  SectionId = "general",  Title = "Текст последней песни — разбор",       Content = "Глубокий анализ текстов и метафор",             AuthorId = "4", AuthorName = "Мария",     IsPinned = false, ReplyCount = 31, CreatedAt = new DateTime(2024, 2, 21),       UpdatedAt = new DateTime(2024, 2, 23, 10, 0, 0) },
            // music
            new ForumTopicDto { Id = "t5",  SectionId = "music",    Title = "Каверы на AloeVera — делимся",         Content = "Скидывайте свои каверы!",                       AuthorId = "1", AuthorName = "Александр", IsPinned = false, ReplyCount = 15, CreatedAt = new DateTime(2024, 2, 20),       UpdatedAt = new DateTime(2024, 2, 22, 20, 15, 0) },
            new ForumTopicDto { Id = "t6",  SectionId = "music",    Title = "Аккорды и табы для гитары",            Content = "Собираем аккорды ко всем песням",               AuthorId = "2", AuthorName = "Дмитрий",   IsPinned = true,  ReplyCount = 8,  CreatedAt = new DateTime(2024, 2, 18),       UpdatedAt = new DateTime(2024, 2, 21, 14, 20, 0) },
            new ForumTopicDto { Id = "t7",  SectionId = "music",    Title = "Плейлисты похожих исполнителей",       Content = "Если вам нравится AloeVera, послушайте...",     AuthorId = "3", AuthorName = "София",     IsPinned = false, ReplyCount = 22, CreatedAt = new DateTime(2024, 2, 19),       UpdatedAt = new DateTime(2024, 2, 22, 18, 0, 0) },
            // cities
            new ForumTopicDto { Id = "t8",  SectionId = "cities",   Title = "Москва — встречи фанатов",             Content = "Организуем встречи в Москве",                   AuthorId = "1", AuthorName = "Анна",      IsPinned = false, ReplyCount = 35, CreatedAt = new DateTime(2024, 2, 20),       UpdatedAt = new DateTime(2024, 2, 23, 8, 0, 0) },
            new ForumTopicDto { Id = "t9",  SectionId = "cities",   Title = "Санкт-Петербург — кто тут?",           Content = "Питерские фанаты, объединяемся!",               AuthorId = "2", AuthorName = "Дмитрий",   IsPinned = false, ReplyCount = 19, CreatedAt = new DateTime(2024, 2, 19),       UpdatedAt = new DateTime(2024, 2, 22, 14, 20, 0) },
            new ForumTopicDto { Id = "t10", SectionId = "cities",   Title = "Новосибирск — ищем компанию на концерт", Content = "Ищем попутчиков",                             AuthorId = "3", AuthorName = "Елена",     IsPinned = false, ReplyCount = 7,  CreatedAt = new DateTime(2024, 2, 18),       UpdatedAt = new DateTime(2024, 2, 21, 12, 0, 0) },
            // offtopic
            new ForumTopicDto { Id = "t11", SectionId = "offtopic", Title = "Кто смотрел новый фильм?",             Content = "Обсуждаем кино и сериалы",                      AuthorId = "1", AuthorName = "Алексей",   IsPinned = false, ReplyCount = 12, CreatedAt = new DateTime(2024, 2, 20),       UpdatedAt = new DateTime(2024, 2, 22, 20, 15, 0) },
            new ForumTopicDto { Id = "t12", SectionId = "offtopic", Title = "Рекомендации книг",                    Content = "Что почитать?",                                 AuthorId = "4", AuthorName = "Мария",     IsPinned = false, ReplyCount = 9,  CreatedAt = new DateTime(2024, 2, 19),       UpdatedAt = new DateTime(2024, 2, 21, 18, 30, 0) },
        });

        MockDataStore.ForumReplies.AddRange(new[]
        {
            // t1 — Какая ваша любимая песня AloeVera?
            new ForumReplyDto { Id = "r1",  TopicId = "t1",  AuthorId = "2", AuthorName = "Дмитрий", Content = "Однозначно \"На краю\"! Мурашки каждый раз.",                         CreatedAt = new DateTime(2024, 2, 20, 13, 10, 0), Likes = 12 },
            new ForumReplyDto { Id = "r2",  TopicId = "t1",  AuthorId = "3", AuthorName = "Елена",   Content = "А мне \"Розовый закат\" больше всего зашёл. Атмосфера потрясающая.", CreatedAt = new DateTime(2024, 2, 20, 15, 30, 0), Likes = 8 },
            new ForumReplyDto { Id = "r3",  TopicId = "t1",  AuthorId = "4", AuthorName = "Мария",   Content = "Согласна с Анной! \"Сладкая жизнь\" — шедевр. Особенно припев.",     CreatedAt = new DateTime(2024, 2, 21, 9, 0, 0),   Likes = 5 },
            new ForumReplyDto { Id = "r4",  TopicId = "t1",  AuthorName = "Алексей",                 Content = "Для меня это \"Ночной город\". Слушаю на повторе уже месяц.",        CreatedAt = new DateTime(2024, 2, 22, 18, 45, 0), Likes = 15 },
            new ForumReplyDto { Id = "r5",  TopicId = "t1",  AuthorName = "София",                   Content = "Сложно выбрать одну! Но если надо — \"Между нами\".",                CreatedAt = new DateTime(2024, 2, 23, 9, 15, 0),  Likes = 3 },
            // t2 — Новый альбом
            new ForumReplyDto { Id = "r6",  TopicId = "t2",  AuthorId = "1", AuthorName = "Анна",    Content = "Послушала три раза подряд! Каждый трек — огонь 🔥",                  CreatedAt = new DateTime(2024, 2, 22, 9, 30, 0),  Likes = 20 },
            new ForumReplyDto { Id = "r7",  TopicId = "t2",  AuthorId = "3", AuthorName = "Елена",   Content = "Продакшн на высоте. Звук стал более зрелым.",                        CreatedAt = new DateTime(2024, 2, 22, 11, 0, 0),  Likes = 14 },
            new ForumReplyDto { Id = "r8",  TopicId = "t2",  AuthorName = "Алексей",                 Content = "Третий трек — мой фаворит. Необычная аранжировка!",                  CreatedAt = new DateTime(2024, 2, 23, 11, 30, 0), Likes = 7 },
            // t3 — Летний фестиваль
            new ForumReplyDto { Id = "r9",  TopicId = "t3",  AuthorId = "4", AuthorName = "Мария",   Content = "Я еду! Уже купила билет 🎉",                                         CreatedAt = new DateTime(2024, 2, 19, 12, 0, 0),  Likes = 6 },
            new ForumReplyDto { Id = "r10", TopicId = "t3",  AuthorId = "2", AuthorName = "Дмитрий", Content = "Тоже планирую. Можно снять жильё вместе?",                           CreatedAt = new DateTime(2024, 2, 20, 8, 30, 0),  Likes = 4 },
            // t4 — Текст последней песни
            new ForumReplyDto { Id = "r11", TopicId = "t4",  AuthorName = "София",                   Content = "Мне кажется, второй куплет — про принятие себя.",                    CreatedAt = new DateTime(2024, 2, 21, 16, 0, 0),  Likes = 11 },
            new ForumReplyDto { Id = "r12", TopicId = "t4",  AuthorName = "Алексей",                 Content = "А припев — отсылка к их ранним работам!",                            CreatedAt = new DateTime(2024, 2, 22, 10, 0, 0),  Likes = 9 },
            // t5 — Каверы
            new ForumReplyDto { Id = "r13", TopicId = "t5",  AuthorId = "2", AuthorName = "Дмитрий", Content = "Вот мой кавер на гитаре: [ссылка]. Не судите строго 😅",             CreatedAt = new DateTime(2024, 2, 18, 15, 0, 0),  Likes = 18 },
            new ForumReplyDto { Id = "r14", TopicId = "t5",  AuthorId = "1", AuthorName = "Анна",    Content = "Круто! А я пою — может запишем коллаб?",                             CreatedAt = new DateTime(2024, 2, 19, 9, 0, 0),   Likes = 10 },
            // t6 — Аккорды
            new ForumReplyDto { Id = "r15", TopicId = "t6",  AuthorName = "Александр",               Content = "\"Сладкая жизнь\": Am - F - C - G, каподастр на 2-м ладу.",         CreatedAt = new DateTime(2024, 2, 15, 12, 0, 0),  Likes = 22 },
            // t7 — Похожие исполнители
            new ForumReplyDto { Id = "r16", TopicId = "t7",  AuthorId = "3", AuthorName = "Елена",   Content = "Очень похожий вайб у группы \"Лунный свет\"!",                       CreatedAt = new DateTime(2024, 2, 20, 14, 0, 0),  Likes = 7 },
            new ForumReplyDto { Id = "r17", TopicId = "t7",  AuthorId = "4", AuthorName = "Мария",   Content = "Советую послушать \"Тени\" — та же атмосфера.",                      CreatedAt = new DateTime(2024, 2, 22, 18, 0, 0),  Likes = 5 },
            // t8 — Москва
            new ForumReplyDto { Id = "r18", TopicId = "t8",  AuthorName = "Алексей",                 Content = "Я за! Предлагаю в эту субботу в центре.",                            CreatedAt = new DateTime(2024, 2, 17, 14, 0, 0),  Likes = 8 },
            new ForumReplyDto { Id = "r19", TopicId = "t8",  AuthorId = "2", AuthorName = "Дмитрий", Content = "Может в парке Горького?",                                            CreatedAt = new DateTime(2024, 2, 18, 9, 0, 0),   Likes = 12 },
            // t9 — Петербург
            new ForumReplyDto { Id = "r20", TopicId = "t9",  AuthorName = "София",                   Content = "Я из Питера! Можем встретиться на Невском.",                         CreatedAt = new DateTime(2024, 2, 18, 12, 0, 0),  Likes = 6 },
            // t10 — Новосибирск
            new ForumReplyDto { Id = "r21", TopicId = "t10", AuthorId = "4", AuthorName = "Мария",   Content = "Я тоже иду! Давайте встретимся у входа.",                            CreatedAt = new DateTime(2024, 2, 19, 14, 0, 0),  Likes = 3 },
            // t11 — Фильм
            new ForumReplyDto { Id = "r22", TopicId = "t11", AuthorId = "1", AuthorName = "Анна",    Content = "Да, отличный фильм! Концовка неожиданная.",                          CreatedAt = new DateTime(2024, 2, 20, 20, 0, 0),  Likes = 4 },
            new ForumReplyDto { Id = "r23", TopicId = "t11", AuthorId = "2", AuthorName = "Дмитрий", Content = "Не понравился, если честно. Ожидал большего.",                       CreatedAt = new DateTime(2024, 2, 21, 10, 0, 0),  Likes = 2 },
            // t12 — Книги
            new ForumReplyDto { Id = "r24", TopicId = "t12", AuthorName = "София",                   Content = "Советую \"Маленький принц\" — вечная классика.",                     CreatedAt = new DateTime(2024, 2, 19, 14, 0, 0),  Likes = 8 },
            new ForumReplyDto { Id = "r25", TopicId = "t12", AuthorId = "3", AuthorName = "Елена",   Content = "\"1984\" Оруэлла — очень актуально сейчас.",                         CreatedAt = new DateTime(2024, 2, 20, 9, 0, 0),   Likes = 6 },
        });
    }

    // ── CreateTopic ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTopic_AddsToSection_ReturnsTopic()
    {
        var service = CreateService();

        var result = await service.CreateTopicAsync(
            SectionId, "user1", "TestUser",
            "A valid topic title", "Valid content body that is long enough");

        Assert.NotNull(result);
        Assert.Equal("A valid topic title", result.Title);
        Assert.Equal(SectionId, result.SectionId);
        Assert.False(result.IsPinned);
        Assert.False(result.IsLocked);
        Assert.Equal(0, result.ReplyCount);

        var topics = await service.GetTopicsAsync(SectionId);
        Assert.Contains(topics.Items, t => t.Id == result.Id);
    }

    [Fact]
    public async Task CreateTopic_IncrementsSectionTopicCount()
    {
        var service = CreateService();

        var sectionsBefore = await service.GetSectionsAsync();
        var before = sectionsBefore.First(s => s.Id == SectionId).TopicCount;

        await service.CreateTopicAsync(
            SectionId, "user1", "TestUser",
            "A valid topic title", "Valid content body that is long enough");

        var sectionsAfter = await service.GetSectionsAsync();
        var after = sectionsAfter.First(s => s.Id == SectionId).TopicCount;

        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task CreateTopic_UnknownSection_Throws()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.CreateTopicAsync(
                "no-such-section-id", "user1", "TestUser",
                "A valid topic title", "Valid content body that is long enough"));
    }

    // ── GetTopics ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTopics_ReturnsPinnedFirst()
    {
        var service = CreateService();

        var topicsResult = await service.GetTopicsAsync(SectionId);

        Assert.Contains(topicsResult.Items, t => t.IsPinned);
        Assert.Contains(topicsResult.Items, t => !t.IsPinned);

        // All pinned topics must appear before all unpinned topics
        bool seenUnpinned = false;
        foreach (var topic in topicsResult.Items)
        {
            if (!topic.IsPinned) seenUnpinned = true;
            if (seenUnpinned && topic.IsPinned)
                Assert.Fail($"Pinned topic '{topic.Title}' appeared after an unpinned topic");
        }
    }

    // ── CreateReply ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateReply_IncrementsReplyCount()
    {
        var service = CreateService();

        var topicsResult = await service.GetTopicsAsync(SectionId);
        var topicId = topicsResult.Items.First().Id;
        var before = topicsResult.Items.First().ReplyCount;

        await service.CreateReplyAsync(
            topicId, "user1", "TestUser",
            "This is a reply with enough content to be valid");

        topicsResult = await service.GetTopicsAsync(SectionId);
        var after = topicsResult.Items.First(t => t.Id == topicId).ReplyCount;

        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task CreateReplyAsync_WithImageUrls_StoresAndReturnsThem()
    {
        var service = CreateService();
        var imageUrls = new List<string> { "https://example.com/photo.jpg" };

        var reply = await service.CreateReplyAsync(
            "t1", "user1", "TestUser",
            "Reply with a photo attached", imageUrls);

        Assert.Equal(imageUrls, reply.ImageUrls);
    }

    [Fact]
    public async Task CreateReply_IncrementsAuthorReplyCount()
    {
        MockDataStore.UserActivity.Clear();
        var userSvc = new MockUserService(new MockAppConfigService());
        var service = new MockForumService(userSvc, new MockEventService(userSvc));

        await service.CreateReplyAsync("t1", "1", "Тест", "Some reply content that's fine.", null);

        var user = await userSvc.GetUserByIdAsync("1");
        Assert.Equal(1, MockDataStore.UserActivity.TryGetValue("1", out var a) ? a.ReplyCount : 0);
        // With only 1 reply and no other activity, user is still novice (threshold 5)
        Assert.Equal(UserRank.Novice, user!.Rank);
        MockDataStore.UserActivity.Clear();
    }

    // ── GetReplies pagination ─────────────────────────────────────────────────

    [Fact]
    public async Task GetReplies_NoCursor_ReturnsInitialPage_NewestFirst()
    {
        var service = CreateService();
        // seed 22 replies (more than RepliesInitial default of 20)
        var topic = await service.CreateTopicAsync(SectionId, "u1", "User One", "Paginate replies", "Content for pagination test");
        for (int i = 0; i < 22; i++)
            await service.CreateReplyAsync(topic.Id, "u1", "User One", $"reply {i}");

        var result = await service.GetRepliesAsync(topic.Id);
        Assert.Equal(20, result.Items.Count);
        Assert.True(result.HasMore);
        Assert.NotNull(result.NextCursor);
        // newest first
        var times = result.Items.Select(r => r.CreatedAt).ToList();
        for (int i = 0; i < times.Count - 1; i++)
            Assert.True(times[i] >= times[i + 1]);
    }

    [Fact]
    public async Task GetReplies_WithCursor_ReturnsOlderBatch()
    {
        var service = CreateService();
        var topic = await service.CreateTopicAsync(SectionId, "u1", "User One", "Cursor replies", "Content for cursor test");
        for (int i = 0; i < 25; i++)
            await service.CreateReplyAsync(topic.Id, "u1", "User One", $"reply {i}");

        var page1 = await service.GetRepliesAsync(topic.Id);
        var page2 = await service.GetRepliesAsync(topic.Id, page1.NextCursor);

        Assert.NotEmpty(page2.Items);
        var page1OldestTime = page1.Items.Last().CreatedAt;
        Assert.All(page2.Items, r => Assert.True(r.CreatedAt <= page1OldestTime));
    }

    // ── GetTopics pagination ──────────────────────────────────────────────────

    [Fact]
    public async Task GetTopics_Page1_ReturnsTopicsInitialCount()
    {
        var service = CreateService();
        for (int i = 0; i < 27; i++)
            await service.CreateTopicAsync(SectionId, "u1", "User One", $"topic {i}", "Content body");

        var result = await service.GetTopicsAsync(SectionId, page: 1);
        Assert.Equal(25, result.Items.Count);   // TopicsInitial default
        Assert.True(result.HasMore);
        Assert.Equal(31, result.Total);
    }

    [Fact]
    public async Task GetTopics_Page2_ReturnsNextBatch()
    {
        var service = CreateService();
        for (int i = 0; i < 30; i++)
            await service.CreateTopicAsync(SectionId, "u1", "User One", $"topic {i}", "Content body");

        var page2 = await service.GetTopicsAsync(SectionId, page: 2);
        Assert.Equal(9, page2.Items.Count);    // 34 total (4 seeded + 30) - 25 = 9 on page 2
        Assert.False(page2.HasMore);
    }

    [Fact]
    public async Task GetTopics_ReturnsPinnedFirst_Paginated()
    {
        var service = CreateService();
        for (int i = 0; i < 3; i++)
            await service.CreateTopicAsync(SectionId, "u1", "User One", $"regular {i}", "Content body");
        var pinned = await service.CreateTopicAsync(SectionId, "u1", "User One", "pinned", "Content body");

        var stored = MockDataStore.ForumTopics.First(t => t.Id == pinned.Id);
        stored.IsPinned = true;

        var result = await service.GetTopicsAsync(SectionId, 1);
        Assert.True(result.Items[0].IsPinned);
    }
}
