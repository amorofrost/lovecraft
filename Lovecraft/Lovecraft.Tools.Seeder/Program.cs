using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.Enums;

// ─── Load connection string ───────────────────────────────────────────────────

var envFile = FindEnvFile();
if (envFile != null)
{
    Console.WriteLine($"Loading .env from: {envFile}");
    LoadEnvFile(envFile);
}

var connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
    ?? throw new InvalidOperationException(
        "AZURE_STORAGE_CONNECTION_STRING not set. " +
        "Add it to a .env file in the solution root or export it as an environment variable.");

// Optional table name prefix (e.g. "dev_", "test2_")
// Set AZURE_TABLE_PREFIX in .env or environment to isolate table sets
var tablePrefix = Environment.GetEnvironmentVariable("AZURE_TABLE_PREFIX") ?? string.Empty;
Lovecraft.Backend.Storage.TableNames.Prefix = tablePrefix;

// ─── Connect ──────────────────────────────────────────────────────────────────

Console.WriteLine("Connecting to Azure Table Storage...\n");
if (!string.IsNullOrEmpty(tablePrefix))
    Console.WriteLine($"Table prefix: \"{tablePrefix}\"\n");
var service = new TableServiceClient(connectionString);

// ─── Reset all known tables (create if needed, then wipe all entities) ────────

var allTables = new[]
{
    TableNames.Users,
    TableNames.UserEmailIndex,
    TableNames.RefreshTokens,
    TableNames.AuthTokens,
    TableNames.Events,
    TableNames.EventAttendees,
    TableNames.Likes,
    TableNames.LikesReceived,
    TableNames.Matches,
    TableNames.BlogPosts,
    TableNames.StoreItems,
    TableNames.ForumSections,
    TableNames.ForumTopics,
    TableNames.ForumTopicIndex,
    TableNames.ForumReplies,
    TableNames.AppConfig,
    TableNames.EventInvites,
};

Console.WriteLine("Resetting tables (ensure exists, then wipe entities)...");
foreach (var name in allTables)
    await ResetTableAsync(service, name);

Console.WriteLine();

// ─── Seed ─────────────────────────────────────────────────────────────────────

const string SeedPassword = "Seed123!@#";
const string TestPassword = "Test123!@#";

// Users
var usersTable      = service.GetTableClient(TableNames.Users);
var emailIndexTable = service.GetTableClient(TableNames.UserEmailIndex);

var seededUsers = new List<(string id, string email, string password)>();

// The well-known test user (mirrors MockAuthService.SeedTestUsers)
await SeedUserAsync(
    usersTable, emailIndexTable,
    userId: "test-user-001",
    email: "test@example.com",
    name: "Test User",
    password: TestPassword,
    age: 25, location: "Москва", gender: Gender.PreferNotToSay, bio: "Test account",
    emailVerified: true,
    staffRole: "admin");
seededUsers.Add(("test-user-001", "test@example.com", TestPassword));

// Mock users from MockDataStore
foreach (var u in MockDataStore.Users)
{
    var email = $"user{u.Id}@mock.local";

    // Per-user activity + staffRole per spec §Seeder table (see plan P7-T2)
    // ids 1/2/3/4 are Anna/Dmitry/Elena/Maria
    var (replyCount, likesReceived, eventsAttended, matchCount, staffRole) = u.Id switch
    {
        "1" => (120, 60, 12, 11, "none"),       // Anna     → Aloe Crew
        "2" => (30,  18,  4,  0, "moderator"),  // Dmitry   → Friend of Aloe, demo moderator
        "3" => (8,    4,  2,  0, "none"),       // Elena    → Active Member
        "4" => (1,    0,  0,  0, "none"),       // Maria    → Novice
        _   => (0,    0,  0,  0, "none"),
    };

    await SeedUserAsync(
        usersTable, emailIndexTable,
        userId: u.Id,
        email: email,
        name: u.Name,
        password: SeedPassword,
        age: u.Age, location: u.Location, gender: u.Gender, bio: u.Bio,
        emailVerified: true,
        profileImage: u.ProfileImage,
        images: u.Images,
        isOnline: u.IsOnline,
        lastSeen: u.LastSeen,
        preferences: u.Preferences,
        settings: u.Settings,
        favoriteSong: u.FavoriteSong,
        replyCount: replyCount,
        likesReceived: likesReceived,
        eventsAttended: eventsAttended,
        matchCount: matchCount,
        staffRole: staffRole);

    seededUsers.Add((u.Id, email, SeedPassword));
}
Console.WriteLine($"  [users]         {seededUsers.Count} users + {seededUsers.Count} email index entries");

// Events + attendees
var eventsTable    = service.GetTableClient(TableNames.Events);
var attendeesTable = service.GetTableClient(TableNames.EventAttendees);
int attendeeCount  = 0;

foreach (var ev in MockDataStore.Events)
{
    var entity = new EventEntity
    {
        PartitionKey = "EVENTS",
        RowKey       = ev.Id,
        Title        = ev.Title,
        Description  = ev.Description,
        ImageUrl     = ev.ImageUrl,
        Date         = DateTime.SpecifyKind(ev.Date, DateTimeKind.Utc),
        EndDate      = ev.EndDate.HasValue ? DateTime.SpecifyKind(ev.EndDate.Value, DateTimeKind.Utc) : null,
        Location     = ev.Location,
        Capacity     = ev.Capacity,
        Category     = ev.Category.ToString(),
        Price        = ev.Price.HasValue ? (double?)Convert.ToDouble(ev.Price.Value) : null,
        Organizer    = ev.Organizer,
        IsSecret     = ev.IsSecret,
        Visibility   = ev.Visibility.ToString(),
        ForumTopicId = ev.ForumTopicId,
    };
    await eventsTable.UpsertEntityAsync(entity);

    foreach (var attendeeId in ev.Attendees)
    {
        var att = new EventAttendeeEntity
        {
            PartitionKey = ev.Id,
            RowKey       = attendeeId,
            RegisteredAt = DateTime.UtcNow,
        };
        await attendeesTable.UpsertEntityAsync(att);
        attendeeCount++;
    }
}
Console.WriteLine($"  [events]        {MockDataStore.Events.Count} events");
Console.WriteLine($"  [eventattendees]{attendeeCount} registrations");

// Event invites (hashed RowKey — plaintext printed in summary; pepper = JWT_SECRET | JWT_SECRET_KEY)
var invitePepper = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
var seededInvitePairs = new (string Plain, string EventId)[]
{
    ("SEED-ALOE-CONCERT-1", "1"),
    ("SEED-ALOE-MEETUP-2", "2"),
    ("SEED-ALOE-FEST-3", "3"),
    ("SEED-ALOE-YACHT-9", "9"),
    ("SEED-ALOE-ACOUSTIC-10", "10"),
    ("SEED-ALOE-STUDIO-11", "11"),
};
if (string.IsNullOrEmpty(invitePepper))
{
    Console.WriteLine("  [eventinvites]  skipped (set JWT_SECRET or JWT_SECRET_KEY in .env for hashed invite rows)");
}
else
{
    var invitesTableSeed = service.GetTableClient(TableNames.EventInvites);
    var inviteExpiry = DateTime.UtcNow.AddYears(2);
    foreach (var (plain, eventId) in seededInvitePairs)
    {
        var hash = EventInviteHasher.Hash(plain, invitePepper);
        await invitesTableSeed.UpsertEntityAsync(new EventInviteEntity
        {
            PartitionKey = EventInviteEntity.PartitionValue,
            RowKey = hash,
            EventId = eventId,
            ExpiresAtUtc = inviteExpiry,
            Revoked = false,
            CreatedAtUtc = DateTime.UtcNow,
        });
    }
    Console.WriteLine($"  [eventinvites]  {seededInvitePairs.Length} rows (plaintext codes listed after seed summary)");
}

// Store items
var storeTable = service.GetTableClient(TableNames.StoreItems);
foreach (var item in MockDataStore.StoreItems)
{
    await storeTable.UpsertEntityAsync(new StoreItemEntity
    {
        PartitionKey        = "STORE",
        RowKey              = item.Id,
        Title               = item.Title,
        Description         = item.Description,
        Price               = Convert.ToDouble(item.Price),
        ImageUrl            = item.ImageUrl,
        Category            = item.Category,
        ExternalPurchaseUrl = item.ExternalPurchaseUrl,
    });
}
Console.WriteLine($"  [storeitems]    {MockDataStore.StoreItems.Count} items");

// Blog posts
var blogTable = service.GetTableClient(TableNames.BlogPosts);
foreach (var post in MockDataStore.BlogPosts)
{
    var date = DateTime.SpecifyKind(post.Date, DateTimeKind.Utc);
    await blogTable.UpsertEntityAsync(new BlogPostEntity
    {
        PartitionKey = "BLOG",
        RowKey       = BlogPostEntity.BuildRowKey(date, post.Id),
        PostId       = post.Id,
        Title        = post.Title,
        Excerpt      = post.Excerpt,
        Content      = post.Content,
        ImageUrl     = post.ImageUrl,
        Author       = post.Author,
        TagsJson     = JsonSerializer.Serialize(post.Tags),
        Date         = date,
    });
}
Console.WriteLine($"  [blogposts]     {MockDataStore.BlogPosts.Count} posts");

// Forum sections
var sectionsTable = service.GetTableClient(TableNames.ForumSections);
foreach (var (section, order) in MockDataStore.ForumSections.Select((s, i) => (s, i)))
{
    await sectionsTable.UpsertEntityAsync(new ForumSectionEntity
    {
        PartitionKey = "FORUM",
        RowKey       = section.Id,
        Name         = section.Name,
        Description  = section.Description,
        TopicCount   = section.TopicCount,
        OrderIndex   = order,
        MinRank      = section.MinRank,
    });
}
Console.WriteLine($"  [forumsections] {MockDataStore.ForumSections.Count} sections");

// Forum topics + index
var topicsTable    = service.GetTableClient(TableNames.ForumTopics);
var topicIdxTable  = service.GetTableClient(TableNames.ForumTopicIndex);

foreach (var topic in MockDataStore.ForumTopics)
{
    var topicEntity = new ForumTopicEntity
    {
        PartitionKey = ForumTopicEntity.GetPartitionKey(topic.SectionId),
        RowKey       = topic.Id,
        SectionId    = topic.SectionId,
        EventId      = topic.EventId ?? string.Empty,
        Title        = topic.Title,
        Content      = topic.Content,
        AuthorId     = topic.AuthorId,
        AuthorName   = topic.AuthorName,
        AuthorAvatar = topic.AuthorAvatar ?? string.Empty,
        IsPinned     = topic.IsPinned,
        IsLocked     = topic.IsLocked,
        ReplyCount   = topic.ReplyCount,
        CreatedAt    = DateTime.SpecifyKind(topic.CreatedAt, DateTimeKind.Utc),
        UpdatedAt    = DateTime.SpecifyKind(topic.UpdatedAt, DateTimeKind.Utc),
    };
    var indexEntity = new ForumTopicIndexEntity
    {
        PartitionKey = "TOPICINDEX",
        RowKey       = topic.Id,
        SectionId    = topic.SectionId,
    };
    await Task.WhenAll(
        topicsTable.UpsertEntityAsync(topicEntity),
        topicIdxTable.UpsertEntityAsync(indexEntity));
}
Console.WriteLine($"  [forumtopics]   {MockDataStore.ForumTopics.Count} topics");
Console.WriteLine($"  [forumtopicindex] {MockDataStore.ForumTopics.Count} index entries");

// Forum replies
var repliesTable = service.GetTableClient(TableNames.ForumReplies);
foreach (var reply in MockDataStore.ForumReplies)
{
    var createdAt = DateTime.SpecifyKind(reply.CreatedAt, DateTimeKind.Utc);
    await repliesTable.UpsertEntityAsync(new ForumReplyEntity
    {
        PartitionKey = ForumReplyEntity.GetPartitionKey(reply.TopicId),
        RowKey       = ForumReplyEntity.BuildRowKey(createdAt, reply.Id),
        ReplyId      = reply.Id,
        TopicId      = reply.TopicId,
        AuthorId     = reply.AuthorId ?? string.Empty,
        AuthorName   = reply.AuthorName,
        AuthorAvatar = reply.AuthorAvatar ?? string.Empty,
        Content      = reply.Content,
        CreatedAt    = createdAt,
        Likes        = reply.Likes,
    });
}
Console.WriteLine($"  [forumreplies]  {MockDataStore.ForumReplies.Count} replies");

// AppConfig (rank_thresholds + permissions)
Console.WriteLine("Seeding appconfig...");
var appConfigTable = service.GetTableClient(TableNames.AppConfig);

async Task UpsertAppConfigAsync(string pk, string rk, string value) =>
    await appConfigTable.UpsertEntityAsync(new AppConfigEntity
    {
        PartitionKey = pk,
        RowKey = rk,
        Value = value,
    });

// rank_thresholds
await UpsertAppConfigAsync("rank_thresholds", "active_replies", "5");
await UpsertAppConfigAsync("rank_thresholds", "active_likes", "3");
await UpsertAppConfigAsync("rank_thresholds", "active_events", "1");
await UpsertAppConfigAsync("rank_thresholds", "friend_replies", "25");
await UpsertAppConfigAsync("rank_thresholds", "friend_likes", "15");
await UpsertAppConfigAsync("rank_thresholds", "friend_events", "3");
await UpsertAppConfigAsync("rank_thresholds", "crew_replies", "100");
await UpsertAppConfigAsync("rank_thresholds", "crew_likes", "50");
await UpsertAppConfigAsync("rank_thresholds", "crew_events", "10");
await UpsertAppConfigAsync("rank_thresholds", "crew_matches", "10");

// permissions
await UpsertAppConfigAsync("permissions", "create_topic", "activeMember");
await UpsertAppConfigAsync("permissions", "delete_own_reply", "novice");
await UpsertAppConfigAsync("permissions", "delete_any_reply", "moderator");
await UpsertAppConfigAsync("permissions", "delete_any_topic", "moderator");
await UpsertAppConfigAsync("permissions", "pin_topic", "moderator");
await UpsertAppConfigAsync("permissions", "ban_user", "moderator");
await UpsertAppConfigAsync("permissions", "assign_role", "admin");
await UpsertAppConfigAsync("permissions", "override_rank", "admin");
await UpsertAppConfigAsync("permissions", "manage_events", "admin");
await UpsertAppConfigAsync("permissions", "manage_blog", "admin");
await UpsertAppConfigAsync("permissions", "manage_store", "admin");

// registration (site-wide — see spec: require_event_invite)
await UpsertAppConfigAsync("registration", "require_event_invite", "false");
Console.WriteLine("  [appconfig]     10 rank_thresholds + 11 permissions + 1 registration");

// Likes + LikesReceived
// Scenarios:
//   test-user-001 → "1" (Anna)      sent only
//   test-user-001 ↔ "2" (Dmitry)    mutual match
//   "1" (Anna)    → test-user-001   received only
//   "3" (Elena)   → test-user-001   received only
//   "1" (Anna)    ↔ "9" (Maria)     mutual match
//   "3" (Elena)   → "2" (Dmitry)    sent only (no reciprocation)
var likesTable         = service.GetTableClient(TableNames.Likes);
var likesReceivedTable = service.GetTableClient(TableNames.LikesReceived);

var likeEdges = new[]
{
    //                fromUserId       toUserId         isMatch
    (from: "test-user-001", to: "1",             mutual: false),
    (from: "test-user-001", to: "2",             mutual: true),
    (from: "2",             to: "test-user-001", mutual: true),
    (from: "1",             to: "test-user-001", mutual: false),
    (from: "3",             to: "test-user-001", mutual: false),
    (from: "1",             to: "9",             mutual: true),
    (from: "9",             to: "1",             mutual: true),
    (from: "3",             to: "2",             mutual: false),
};

var likesSeedTime = DateTime.UtcNow;
foreach (var (from, to, mutual) in likeEdges)
{
    var likeId = $"{from}_{to}";
    await SeedLikeAsync(likesTable, likesReceivedTable, likeId, from, to, mutual, likesSeedTime);
}

Console.WriteLine($"  [likes]         {likeEdges.Length} rows");
Console.WriteLine($"  [likesreceived] {likeEdges.Length} rows");

// Remaining tables (refreshtokens, authtokens, matches) stay empty
Console.WriteLine($"  [refreshtokens / authtokens / matches] empty (ready to use)");

// ─── Summary ──────────────────────────────────────────────────────────────────

Console.WriteLine("\n✓ Seeding complete.\n");
if (!string.IsNullOrEmpty(invitePepper))
{
    Console.WriteLine("Seeded event invite codes (use at registration or for secret event access):");
    Console.WriteLine("  Plaintext          EventId  Notes");
    foreach (var (plain, eventId) in seededInvitePairs)
        Console.WriteLine($"  {plain,-20} {eventId,-8} mock seed");
    Console.WriteLine();
}
Console.WriteLine("Test credentials:");
foreach (var (id, email, pw) in seededUsers)
    Console.WriteLine($"  {email,-38} password: {pw}   (id: {id})");

// ─── Helpers ──────────────────────────────────────────────────────────────────

static async Task SeedLikeAsync(
    TableClient likesTable,
    TableClient likesReceivedTable,
    string likeId,
    string fromUserId,
    string toUserId,
    bool isMatch,
    DateTime createdAt)
{
    // likes table:         PK = fromUserId, RK = toUserId
    var likeEntity = new LikeEntity
    {
        PartitionKey = fromUserId,
        RowKey       = toUserId,
        LikeId       = likeId,
        FromUserId   = fromUserId,
        ToUserId     = toUserId,
        CreatedAt    = createdAt,
        IsMatch      = isMatch,
    };

    // likesreceived table: PK = toUserId (recipient), RK = fromUserId (sender)
    var receivedEntity = new LikeEntity
    {
        PartitionKey = toUserId,
        RowKey       = fromUserId,
        LikeId       = likeId,
        FromUserId   = fromUserId,
        ToUserId     = toUserId,
        CreatedAt    = createdAt,
        IsMatch      = isMatch,
    };

    await Task.WhenAll(
        likesTable.UpsertEntityAsync(likeEntity),
        likesReceivedTable.UpsertEntityAsync(receivedEntity));
}

static async Task ResetTableAsync(TableServiceClient service, string name)
{
    var client = service.GetTableClient(name);
    Console.Write($"  {name,-22} ");

    // Create if it doesn't exist yet
    await client.CreateIfNotExistsAsync();

    // Collect all entities grouped by partition (batch deletes require same PK)
    var byPartition = new Dictionary<string, List<TableEntity>>();
    await foreach (var entity in client.QueryAsync<TableEntity>(select: new[] { "PartitionKey", "RowKey" }))
    {
        if (!byPartition.TryGetValue(entity.PartitionKey, out var list))
            byPartition[entity.PartitionKey] = list = new();
        list.Add(entity);
    }

    int total = byPartition.Values.Sum(l => l.Count);
    if (total == 0)
    {
        Console.WriteLine("empty, ready.");
        return;
    }

    // Delete in batches of up to 100 per partition
    int deleted = 0;
    foreach (var (_, entities) in byPartition)
    {
        for (int i = 0; i < entities.Count; i += 100)
        {
            var batch = entities
                .Skip(i).Take(100)
                .Select(e => new TableTransactionAction(TableTransactionActionType.Delete, e))
                .ToList();
            await client.SubmitTransactionAsync(batch);
            deleted += batch.Count;
        }
    }

    Console.WriteLine($"wiped {deleted} entities, ready.");
}

static async Task SeedUserAsync(
    TableClient usersTable,
    TableClient emailIndexTable,
    string userId,
    string email,
    string name,
    string password,
    int age,
    string location,
    Gender gender,
    string bio,
    bool emailVerified,
    string profileImage = "",
    List<string>? images = null,
    bool isOnline = false,
    DateTime? lastSeen = null,
    Lovecraft.Common.DTOs.Users.UserPreferencesDto? preferences = null,
    Lovecraft.Common.DTOs.Users.UserSettingsDto? settings = null,
    Lovecraft.Common.DTOs.Users.AloeVeraSongDto? favoriteSong = null,
    int replyCount = 0,
    int likesReceived = 0,
    int eventsAttended = 0,
    int matchCount = 0,
    string staffRole = "none")
{
    var now = DateTime.UtcNow;
    var userEntity = new UserEntity
    {
        PartitionKey  = UserEntity.GetPartitionKey(userId),
        RowKey        = userId,
        Email         = email,
        PasswordHash  = HashPassword(password),
        Name          = name,
        Age           = age,
        Location      = location,
        Gender        = gender.ToString(),
        Bio           = bio,
        EmailVerified = emailVerified,
        AuthMethodsJson  = JsonSerializer.Serialize(new[] { "local" }),
        PreferencesJson  = JsonSerializer.Serialize(preferences ?? new Lovecraft.Common.DTOs.Users.UserPreferencesDto()),
        SettingsJson     = JsonSerializer.Serialize(settings ?? new Lovecraft.Common.DTOs.Users.UserSettingsDto()),
        FavoriteSongJson = favoriteSong != null ? JsonSerializer.Serialize(favoriteSong) : string.Empty,
        ProfileImage  = profileImage,
        ImagesJson    = JsonSerializer.Serialize(images ?? new List<string>()),
        IsOnline      = isOnline,
        LastSeen      = DateTime.SpecifyKind(lastSeen ?? now, DateTimeKind.Utc),
        CreatedAt     = now,
        UpdatedAt     = now,
        ReplyCount      = replyCount,
        LikesReceived   = likesReceived,
        EventsAttended  = eventsAttended,
        MatchCount      = matchCount,
        StaffRole       = staffRole,
    };

    var indexEntity = new UserEmailIndexEntity
    {
        PartitionKey = email.ToLower(),
        RowKey       = "INDEX",
        UserId       = userId,
    };

    await Task.WhenAll(
        usersTable.UpsertEntityAsync(userEntity),
        emailIndexTable.UpsertEntityAsync(indexEntity));
}

static string HashPassword(string password)
{
    const int SaltSize  = 16;
    const int HashSize  = 32;
    const int Iterations = 100_000;

    using var rng  = RandomNumberGenerator.Create();
    var salt = new byte[SaltSize];
    rng.GetBytes(salt);

#pragma warning disable SYSLIB0060
    using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
#pragma warning restore SYSLIB0060
    var hash = pbkdf2.GetBytes(HashSize);

    var combined = new byte[SaltSize + HashSize];
    Array.Copy(salt, 0, combined, 0, SaltSize);
    Array.Copy(hash, 0, combined, SaltSize, HashSize);
    return Convert.ToBase64String(combined);
}

static string? FindEnvFile()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, ".env");
        if (File.Exists(candidate))
            return candidate;
        dir = dir.Parent;
    }
    return null;
}

static void LoadEnvFile(string path)
{
    foreach (var line in File.ReadAllLines(path))
    {
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            continue;
        var eq = line.IndexOf('=');
        if (eq < 0) continue;
        var key   = line[..eq].Trim();
        var value = line[(eq + 1)..].Trim();
        // Only set if not already set (env var takes priority over .env file)
        if (Environment.GetEnvironmentVariable(key) == null)
            Environment.SetEnvironmentVariable(key, value);
    }
}
