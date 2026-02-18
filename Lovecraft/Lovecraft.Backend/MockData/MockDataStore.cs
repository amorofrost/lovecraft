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

    public static List<ForumSectionDto> ForumSections { get; } = new()
    {
        new() { Id = "general", Name = "Backend Mock: Общий", Description = "Общие обсуждения", TopicCount = 12 },
        new() { Id = "music", Name = "Backend Mock: Музыка", Description = "Обсуждение песен и альбомов", TopicCount = 8 },
        new() { Id = "cities", Name = "Backend Mock: Города", Description = "Региональные темы", TopicCount = 5 },
        new() { Id = "offtopic", Name = "Backend Mock: Офтопик", Description = "Все остальное", TopicCount = 15 },
    };

    public static List<LikeDto> Likes { get; set; } = new();
    public static List<MatchDto> Matches { get; set; } = new();

    // Current user ID for mock authentication
    public const string CurrentUserId = "current-user";
}
