namespace Lovecraft.Backend.Services;

public record AppConfig(RankThresholds Ranks, PermissionConfig Permissions, RegistrationConfig Registration, PaginationConfig Pagination);

/// <summary>
/// Site-wide registration policy (Azure Table appconfig partition <c>registration</c>).
/// </summary>
public record RegistrationConfig(bool RequireEventInvite)
{
    public static RegistrationConfig Defaults => new(RequireEventInvite: false);
}

public record PaginationConfig(
    int MessagesInitial,
    int MessagesBatch,
    int RepliesInitial,
    int RepliesBatch,
    int TopicsInitial,
    int TopicsBatch)
{
    public static PaginationConfig Defaults => new(
        MessagesInitial: 30,
        MessagesBatch:   20,
        RepliesInitial:  20,
        RepliesBatch:    15,
        TopicsInitial:   25,
        TopicsBatch:     15);
}

public record RankThresholds(
    int ActiveReplies,
    int ActiveLikes,
    int ActiveEvents,
    int FriendReplies,
    int FriendLikes,
    int FriendEvents,
    int CrewReplies,
    int CrewLikes,
    int CrewEvents,
    int CrewMatches)
{
    public static RankThresholds Defaults => new(
        ActiveReplies: 5,
        ActiveLikes: 3,
        ActiveEvents: 1,
        FriendReplies: 25,
        FriendLikes: 15,
        FriendEvents: 3,
        CrewReplies: 100,
        CrewLikes: 50,
        CrewEvents: 10,
        CrewMatches: 10);
}

public record PermissionConfig(
    string CreateTopic,
    string DeleteOwnReply,
    string DeleteAnyReply,
    string DeleteAnyTopic,
    string PinTopic,
    string BanUser,
    string AssignRole,
    string OverrideRank,
    string ManageEvents,
    string ManageBlog,
    string ManageStore)
{
    public static PermissionConfig Defaults => new(
        CreateTopic: "activeMember",
        DeleteOwnReply: "novice",
        DeleteAnyReply: "moderator",
        DeleteAnyTopic: "moderator",
        PinTopic: "moderator",
        BanUser: "moderator",
        AssignRole: "admin",
        OverrideRank: "admin",
        ManageEvents: "admin",
        ManageBlog: "admin",
        ManageStore: "admin");
}

public static class AppConfigKeys
{
    public static class RankThresholdsKeys
    {
        public const string ActiveReplies = "active_replies";
        public const string ActiveLikes = "active_likes";
        public const string ActiveEvents = "active_events";
        public const string FriendReplies = "friend_replies";
        public const string FriendLikes = "friend_likes";
        public const string FriendEvents = "friend_events";
        public const string CrewReplies = "crew_replies";
        public const string CrewLikes = "crew_likes";
        public const string CrewEvents = "crew_events";
        public const string CrewMatches = "crew_matches";
    }

    public static class PermissionKeys
    {
        public const string CreateTopic = "create_topic";
        public const string DeleteOwnReply = "delete_own_reply";
        public const string DeleteAnyReply = "delete_any_reply";
        public const string DeleteAnyTopic = "delete_any_topic";
        public const string PinTopic = "pin_topic";
        public const string BanUser = "ban_user";
        public const string AssignRole = "assign_role";
        public const string OverrideRank = "override_rank";
        public const string ManageEvents = "manage_events";
        public const string ManageBlog = "manage_blog";
        public const string ManageStore = "manage_store";
    }

    public static class RegistrationKeys
    {
        public const string RequireEventInvite = "require_event_invite";
    }

    public static class PaginationKeys
    {
        public const string MessagesInitial = "messages_initial";
        public const string MessagesBatch   = "messages_batch";
        public const string RepliesInitial  = "replies_initial";
        public const string RepliesBatch    = "replies_batch";
        public const string TopicsInitial   = "topics_initial";
        public const string TopicsBatch     = "topics_batch";
    }
}
