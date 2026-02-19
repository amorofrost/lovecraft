using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.DTOs.Matching;
using Lovecraft.Common.DTOs.Store;
using Lovecraft.Common.DTOs.Blog;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.MockData;

public static class MockDataStore
{
    public static List<AloeVeraSongDto> Songs { get; } = new()
    {
        new() { Id = "1", Title = "Backend Mock: Звездное небо", Album = "Первый альбом", Duration = "3:45", PreviewUrl = "", Year = 2018 },
        new() { Id = "2", Title = "Backend Mock: Летний ветер", Album = "Первый альбом", Duration = "4:12", PreviewUrl = "", Year = 2018 },
        new() { Id = "3", Title = "Backend Mock: Новые горизонты", Album = "Второй альбом", Duration = "3:28", PreviewUrl = "", Year = 2020 },
    };

    public static List<EventDto> Events { get; } = new()
    {
        new()
        {
            Id = "1",
            Title = "Backend Mock: Концерт AloeVera: Новые горизонты",
            Description = "Эксклюзивный концерт с новыми песнями",
            ImageUrl = "https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?w=800&h=400&fit=crop",
            Date = new DateTime(2024, 12, 15, 19, 0, 0),
            EndDate = new DateTime(2024, 12, 15, 23, 0, 0),
            Location = "Театр \"Мир\", Москва",
            Capacity = 500,
            Attendees = new List<string> { "1", "2", "3" },
            Category = EventCategory.Concert,
            Price = 2500m,
            Organizer = "AloeVera Official",
            IsSecret = false
        },
        new()
        {
            Id = "2",
            Title = "Backend Mock: Фан-встреча: Поэзия и музыка",
            Description = "Неформальная встреча фанатов",
            ImageUrl = "https://images.unsplash.com/photo-1516450360452-9312f5e86fc7?w=800&h=400&fit=crop",
            Date = new DateTime(2024, 11, 8, 15, 0, 0),
            EndDate = new DateTime(2024, 11, 8, 18, 0, 0),
            Location = "Парк Сокольники, Москва",
            Attendees = new List<string> { "4", "5", "6", "7" },
            Category = EventCategory.Meetup,
            Organizer = "Фан-клуб AloeVera",
            IsSecret = false
        },
        new()
        {
            Id = "3",
            Title = "Backend Mock: AloeVera Fest 2024",
            Description = "Большой фестиваль!",
            ImageUrl = "https://images.unsplash.com/photo-1459749411175-04bf5292ceea?w=800&h=400&fit=crop",
            Date = new DateTime(2025, 6, 20, 12, 0, 0),
            EndDate = new DateTime(2025, 6, 21, 23, 0, 0),
            Location = "Лужники, Москва",
            Capacity = 50000,
            Attendees = new List<string> { "8", "9", "10", "11", "12" },
            Category = EventCategory.Festival,
            Price = 5000m,
            Organizer = "AloeVera Official",
            IsSecret = false
        },
        new()
        {
            Id = "9",
            Title = "Backend Mock: Яхтинг в Австралии 2026",
            Description = "Только для тех, кто знает.",
            ImageUrl = "https://images.unsplash.com/photo-1544551763-46a013bb70d5?w=800&h=400&fit=crop",
            Date = new DateTime(2026, 4, 15, 10, 0, 0),
            EndDate = new DateTime(2026, 4, 22, 18, 0, 0),
            Location = "Золотое побережье, Австралия",
            Capacity = 50,
            Attendees = new List<string> { "1", "13", "14", "15" },
            Category = EventCategory.Yachting,
            Price = 25000m,
            Organizer = "Veter Veter",
            IsSecret = true
        },
    };

    public static List<UserDto> Users { get; } = new()
    {
        new()
        {
            Id = "1",
            Name = "Backend Mock: Анна",
            Age = 25,
            Bio = "Обожаю музыку AloeVera и концерты под открытым небом ❤️",
            Location = "Москва",
            Gender = Gender.Female,
            ProfileImage = "https://images.unsplash.com/photo-1438761681033-6461ffad8d80?w=400&h=600&fit=crop&crop=face",
            Images = new List<string>(),
            LastSeen = DateTime.UtcNow,
            IsOnline = true,
            FavoriteSong = Songs[0],
            Preferences = new UserPreferencesDto
            {
                AgeRangeMin = 22,
                AgeRangeMax = 35,
                MaxDistance = 50,
                ShowMe = ShowMePreference.Everyone
            },
            Settings = new UserSettingsDto
            {
                ProfileVisibility = ProfileVisibility.Public,
                AnonymousLikes = false,
                Language = Language.Ru,
                Notifications = true
            }
        },
        new()
        {
            Id = "2",
            Name = "Backend Mock: Дмитрий",
            Age = 28,
            Bio = "Музыкант, фанат AloeVera с первого альбома 🎸",
            Location = "Санкт-Петербург",
            Gender = Gender.Male,
            ProfileImage = "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=400&h=600&fit=crop&crop=face",
            Images = new List<string>(),
            LastSeen = DateTime.UtcNow.AddHours(-2),
            IsOnline = false,
            Preferences = new UserPreferencesDto(),
            Settings = new UserSettingsDto()
        },
        new()
        {
            Id = "3",
            Name = "Backend Mock: Елена",
            Age = 22,
            Bio = "Танцую под AloeVera 💃",
            Location = "Новосибирск",
            Gender = Gender.Female,
            ProfileImage = "https://images.unsplash.com/photo-1438761681033-6461ffad8d80?w=400&h=600&fit=crop&crop=face",
            Images = new List<string>(),
            LastSeen = DateTime.UtcNow,
            IsOnline = true,
            FavoriteSong = Songs[2],
            Preferences = new UserPreferencesDto(),
            Settings = new UserSettingsDto()
        },
        new()
        {
            Id = "4",
            Name = "Backend Mock: Мария",
            Age = 23,
            Bio = "Поэтесса и меломан",
            Location = "Москва",
            Gender = Gender.Female,
            ProfileImage = "https://images.unsplash.com/photo-1438761681033-6461ffad8d80?w=400&h=600&fit=crop&crop=face",
            Images = new List<string>(),
            LastSeen = DateTime.UtcNow.AddMinutes(-30),
            IsOnline = true,
            Preferences = new UserPreferencesDto(),
            Settings = new UserSettingsDto()
        },
    };

    public static List<StoreItemDto> StoreItems { get; } = new()
    {
        new()
        {
            Id = "s1",
            Title = "Backend Mock: Футболка \"Новые горизонты\"",
            Description = "Официальная футболка группы AloeVera",
            Price = 2500m,
            ImageUrl = "https://images.unsplash.com/photo-1521572163474-6864f9cf17ab?w=400&h=400&fit=crop",
            Category = "Одежда",
            ExternalPurchaseUrl = "https://aloemore.ru/store/tshirt-1"
        },
        new()
        {
            Id = "s2",
            Title = "Backend Mock: Виниловая пластинка — Первый альбом",
            Description = "Виниловое издание первого альбома",
            Price = 3500m,
            ImageUrl = "https://images.unsplash.com/photo-1539375665275-f9de415ef9ac?w=400&h=400&fit=crop",
            Category = "Музыка",
            ExternalPurchaseUrl = "https://aloemore.ru/store/vinyl-1"
        },
        new()
        {
            Id = "s3",
            Title = "Backend Mock: Постер \"AloeVera Fest 2024\"",
            Description = "Коллекционный постер с фестиваля",
            Price = 800m,
            ImageUrl = "https://images.unsplash.com/photo-1561070791-2526d30994b5?w=400&h=400&fit=crop",
            Category = "Мерч",
            ExternalPurchaseUrl = "https://aloemore.ru/store/poster-1"
        },
        new()
        {
            Id = "s4",
            Title = "Backend Mock: Худи \"AloeVera\"",
            Description = "Теплое худи с логотипом группы",
            Price = 4500m,
            ImageUrl = "https://images.unsplash.com/photo-1556821840-3a63f95609a7?w=400&h=400&fit=crop",
            Category = "Одежда",
            ExternalPurchaseUrl = "https://aloemore.ru/store/hoodie-1"
        },
    };

    public static List<BlogPostDto> BlogPosts { get; } = new()
    {
        new()
        {
            Id = "b1",
            Title = "Backend Mock: За кулисами нового альбома",
            Excerpt = "Эксклюзивный репортаж из студии записи. Как создавался новый звук группы...",
            Content = "Полный текст статьи о создании нового альбома...",
            ImageUrl = "https://images.unsplash.com/photo-1598488035139-bdbb2231ce04?w=800&h=400&fit=crop",
            Author = "AloeVera Team",
            Tags = new List<string> { "Студия", "Альбом" },
            Date = new DateTime(2024, 2, 20)
        },
        new()
        {
            Id = "b2",
            Title = "Backend Mock: Итоги тура 2023",
            Excerpt = "Вспоминаем лучшие моменты прошлогоднего тура по России...",
            Content = "Полный текст статьи об итогах тура...",
            ImageUrl = "https://images.unsplash.com/photo-1501386761578-eac5c94b800a?w=800&h=400&fit=crop",
            Author = "AloeVera Team",
            Tags = new List<string> { "Тур", "Концерт" },
            Date = new DateTime(2024, 1, 15)
        },
        new()
        {
            Id = "b3",
            Title = "Backend Mock: Интервью: О вдохновении и музыке",
            Excerpt = "Большое интервью с участниками группы о творческом процессе...",
            Content = "Полный текст интервью...",
            ImageUrl = "https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?w=800&h=400&fit=crop",
            Author = "Music Magazine",
            Tags = new List<string> { "Интервью", "Альбом" },
            Date = new DateTime(2024, 2, 10)
        },
    };

    public static List<ForumTopicDto> ForumTopics { get; } = new()
    {
        // general
        new() { Id = "t1", SectionId = "general", Title = "Какая ваша любимая песня AloeVera?", Content = "Делитесь любимыми треками!", AuthorId = "1", AuthorName = "Анна", IsPinned = true, ReplyCount = 24, CreatedAt = new DateTime(2024, 2, 20), UpdatedAt = new DateTime(2024, 2, 23, 9, 15, 0) },
        new() { Id = "t2", SectionId = "general", Title = "Новый альбом — ваши впечатления", Content = "Обсуждаем новый альбом группы", AuthorId = "2", AuthorName = "Дмитрий", IsPinned = true, ReplyCount = 42, CreatedAt = new DateTime(2024, 2, 21), UpdatedAt = new DateTime(2024, 2, 23, 11, 30, 0) },
        new() { Id = "t3", SectionId = "general", Title = "Кто едет на летний фестиваль?", Content = "Планируем поездку вместе", AuthorId = "3", AuthorName = "Елена", ReplyCount = 18, CreatedAt = new DateTime(2024, 2, 20), UpdatedAt = new DateTime(2024, 2, 22, 16, 45, 0) },
        new() { Id = "t4", SectionId = "general", Title = "Текст последней песни — разбор", Content = "Глубокий анализ текстов и метафор", AuthorId = "4", AuthorName = "Мария", ReplyCount = 31, CreatedAt = new DateTime(2024, 2, 21), UpdatedAt = new DateTime(2024, 2, 23, 10, 0, 0) },
        // music
        new() { Id = "t5", SectionId = "music", Title = "Каверы на AloeVera — делимся", Content = "Скидывайте свои каверы!", AuthorId = "1", AuthorName = "Александр", ReplyCount = 15, CreatedAt = new DateTime(2024, 2, 20), UpdatedAt = new DateTime(2024, 2, 22, 20, 15, 0) },
        new() { Id = "t6", SectionId = "music", Title = "Аккорды и табы для гитары", Content = "Собираем аккорды ко всем песням", AuthorId = "2", AuthorName = "Дмитрий", IsPinned = true, ReplyCount = 8, CreatedAt = new DateTime(2024, 2, 18), UpdatedAt = new DateTime(2024, 2, 21, 14, 20, 0) },
        new() { Id = "t7", SectionId = "music", Title = "Плейлисты похожих исполнителей", Content = "Если вам нравится AloeVera, послушайте...", AuthorId = "3", AuthorName = "София", ReplyCount = 22, CreatedAt = new DateTime(2024, 2, 19), UpdatedAt = new DateTime(2024, 2, 22, 18, 0, 0) },
        // cities
        new() { Id = "t8", SectionId = "cities", Title = "Москва — встречи фанатов", Content = "Организуем встречи в Москве", AuthorId = "1", AuthorName = "Анна", ReplyCount = 35, CreatedAt = new DateTime(2024, 2, 20), UpdatedAt = new DateTime(2024, 2, 23, 8, 0, 0) },
        new() { Id = "t9", SectionId = "cities", Title = "Санкт-Петербург — кто тут?", Content = "Питерские фанаты, объединяемся!", AuthorId = "2", AuthorName = "Дмитрий", ReplyCount = 19, CreatedAt = new DateTime(2024, 2, 19), UpdatedAt = new DateTime(2024, 2, 22, 14, 20, 0) },
        new() { Id = "t10", SectionId = "cities", Title = "Новосибирск — ищем компанию на концерт", Content = "Ищем попутчиков", AuthorId = "3", AuthorName = "Елена", ReplyCount = 7, CreatedAt = new DateTime(2024, 2, 18), UpdatedAt = new DateTime(2024, 2, 21, 12, 0, 0) },
        // offtopic
        new() { Id = "t11", SectionId = "offtopic", Title = "Кто смотрел новый фильм?", Content = "Обсуждаем кино и сериалы", AuthorId = "1", AuthorName = "Алексей", ReplyCount = 12, CreatedAt = new DateTime(2024, 2, 20), UpdatedAt = new DateTime(2024, 2, 22, 20, 15, 0) },
        new() { Id = "t12", SectionId = "offtopic", Title = "Рекомендации книг", Content = "Что почитать?", AuthorId = "4", AuthorName = "Мария", ReplyCount = 9, CreatedAt = new DateTime(2024, 2, 19), UpdatedAt = new DateTime(2024, 2, 21, 18, 30, 0) },
    };

    public static List<ForumSectionDto> ForumSections { get; } = new()
    {
        new() { Id = "general", Name = "💬 Общие обсуждения", Description = "Свободное общение на любые темы", TopicCount = 4 },
        new() { Id = "music", Name = "🎵 Музыка и творчество", Description = "Разбор песен, каверы, творчество", TopicCount = 3 },
        new() { Id = "cities", Name = "🏙️ По городам", Description = "Общение по городам и регионам", TopicCount = 3 },
        new() { Id = "offtopic", Name = "🎨 Оффтопик", Description = "Всё, что не связано с музыкой", TopicCount = 2 },
    };

    public static List<ForumReplyDto> ForumReplies { get; } = new()
    {
        // t1 — Какая ваша любимая песня AloeVera?
        new() { Id = "r1", TopicId = "t1", AuthorId = "2", AuthorName = "Дмитрий", Content = "Однозначно \"На краю\"! Мурашки каждый раз.", CreatedAt = new DateTime(2024, 2, 20, 13, 10, 0), Likes = 12 },
        new() { Id = "r2", TopicId = "t1", AuthorId = "3", AuthorName = "Елена", Content = "А мне \"Розовый закат\" больше всего зашёл. Атмосфера потрясающая.", CreatedAt = new DateTime(2024, 2, 20, 15, 30, 0), Likes = 8 },
        new() { Id = "r3", TopicId = "t1", AuthorId = "4", AuthorName = "Мария", Content = "Согласна с Анной! \"Сладкая жизнь\" — шедевр. Особенно припев.", CreatedAt = new DateTime(2024, 2, 21, 9, 0, 0), Likes = 5 },
        new() { Id = "r4", TopicId = "t1", AuthorName = "Алексей", Content = "Для меня это \"Ночной город\". Слушаю на повторе уже месяц.", CreatedAt = new DateTime(2024, 2, 22, 18, 45, 0), Likes = 15 },
        new() { Id = "r5", TopicId = "t1", AuthorName = "София", Content = "Сложно выбрать одну! Но если надо — \"Между нами\".", CreatedAt = new DateTime(2024, 2, 23, 9, 15, 0), Likes = 3 },
        // t2 — Новый альбом
        new() { Id = "r6", TopicId = "t2", AuthorId = "1", AuthorName = "Анна", Content = "Послушала три раза подряд! Каждый трек — огонь 🔥", CreatedAt = new DateTime(2024, 2, 22, 9, 30, 0), Likes = 20 },
        new() { Id = "r7", TopicId = "t2", AuthorId = "3", AuthorName = "Елена", Content = "Продакшн на высоте. Звук стал более зрелым.", CreatedAt = new DateTime(2024, 2, 22, 11, 0, 0), Likes = 14 },
        new() { Id = "r8", TopicId = "t2", AuthorName = "Алексей", Content = "Третий трек — мой фаворит. Необычная аранжировка!", CreatedAt = new DateTime(2024, 2, 23, 11, 30, 0), Likes = 7 },
        // t3 — Летний фестиваль
        new() { Id = "r9", TopicId = "t3", AuthorId = "4", AuthorName = "Мария", Content = "Я еду! Уже купила билет 🎉", CreatedAt = new DateTime(2024, 2, 19, 12, 0, 0), Likes = 6 },
        new() { Id = "r10", TopicId = "t3", AuthorId = "2", AuthorName = "Дмитрий", Content = "Тоже планирую. Можно снять жильё вместе?", CreatedAt = new DateTime(2024, 2, 20, 8, 30, 0), Likes = 4 },
        // t4 — Текст последней песни
        new() { Id = "r11", TopicId = "t4", AuthorName = "София", Content = "Мне кажется, второй куплет — про принятие себя.", CreatedAt = new DateTime(2024, 2, 21, 16, 0, 0), Likes = 11 },
        new() { Id = "r12", TopicId = "t4", AuthorName = "Алексей", Content = "А припев — отсылка к их ранним работам!", CreatedAt = new DateTime(2024, 2, 22, 10, 0, 0), Likes = 9 },
        // t5 — Каверы
        new() { Id = "r13", TopicId = "t5", AuthorId = "2", AuthorName = "Дмитрий", Content = "Вот мой кавер на гитаре: [ссылка]. Не судите строго 😅", CreatedAt = new DateTime(2024, 2, 18, 15, 0, 0), Likes = 18 },
        new() { Id = "r14", TopicId = "t5", AuthorId = "1", AuthorName = "Анна", Content = "Круто! А я пою — может запишем коллаб?", CreatedAt = new DateTime(2024, 2, 19, 9, 0, 0), Likes = 10 },
        // t6 — Аккорды
        new() { Id = "r15", TopicId = "t6", AuthorName = "Александр", Content = "\"Сладкая жизнь\": Am - F - C - G, каподастр на 2-м ладу.", CreatedAt = new DateTime(2024, 2, 15, 12, 0, 0), Likes = 22 },
        // t7 — Похожие исполнители
        new() { Id = "r16", TopicId = "t7", AuthorId = "3", AuthorName = "Елена", Content = "Очень похожий вайб у группы \"Лунный свет\"!", CreatedAt = new DateTime(2024, 2, 20, 14, 0, 0), Likes = 7 },
        new() { Id = "r17", TopicId = "t7", AuthorId = "4", AuthorName = "Мария", Content = "Советую послушать \"Тени\" — та же атмосфера.", CreatedAt = new DateTime(2024, 2, 22, 18, 0, 0), Likes = 5 },
        // t8 — Москва
        new() { Id = "r18", TopicId = "t8", AuthorName = "Алексей", Content = "Я за! Предлагаю в эту субботу в центре.", CreatedAt = new DateTime(2024, 2, 17, 14, 0, 0), Likes = 8 },
        new() { Id = "r19", TopicId = "t8", AuthorId = "2", AuthorName = "Дмитрий", Content = "Может в парке Горького?", CreatedAt = new DateTime(2024, 2, 18, 9, 0, 0), Likes = 12 },
        // t9 — Петербург
        new() { Id = "r20", TopicId = "t9", AuthorName = "София", Content = "Я из Питера! Можем встретиться на Невском.", CreatedAt = new DateTime(2024, 2, 18, 12, 0, 0), Likes = 6 },
        // t10 — Новосибирск
        new() { Id = "r21", TopicId = "t10", AuthorId = "4", AuthorName = "Мария", Content = "Я тоже иду! Давайте встретимся у входа.", CreatedAt = new DateTime(2024, 2, 19, 14, 0, 0), Likes = 3 },
        // t11 — Фильм
        new() { Id = "r22", TopicId = "t11", AuthorId = "1", AuthorName = "Анна", Content = "Да, отличный фильм! Концовка неожиданная.", CreatedAt = new DateTime(2024, 2, 20, 20, 0, 0), Likes = 4 },
        new() { Id = "r23", TopicId = "t11", AuthorId = "2", AuthorName = "Дмитрий", Content = "Не понравился, если честно. Ожидал большего.", CreatedAt = new DateTime(2024, 2, 21, 10, 0, 0), Likes = 2 },
        // t12 — Книги
        new() { Id = "r24", TopicId = "t12", AuthorName = "София", Content = "Советую \"Маленький принц\" — вечная классика.", CreatedAt = new DateTime(2024, 2, 19, 14, 0, 0), Likes = 8 },
        new() { Id = "r25", TopicId = "t12", AuthorId = "3", AuthorName = "Елена", Content = "\"1984\" Оруэлла — очень актуально сейчас.", CreatedAt = new DateTime(2024, 2, 20, 9, 0, 0), Likes = 6 },
    };

    public static List<LikeDto> Likes { get; set; } = new();
    public static List<MatchDto> Matches { get; set; } = new();

    // Current user ID for mock authentication
    public const string CurrentUserId = "current-user";
}
