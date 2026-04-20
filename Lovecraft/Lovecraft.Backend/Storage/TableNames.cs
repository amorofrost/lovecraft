namespace Lovecraft.Backend.Storage;

public static class TableNames
{
    /// <summary>
    /// Optional prefix prepended to every table name.
    /// Set once at startup via AZURE_TABLE_PREFIX env var (e.g. "dev_", "test_").
    /// Defaults to empty string (no prefix).
    /// </summary>
    public static string Prefix { get; set; } = string.Empty;

    public static string Users           => Prefix + "users";
    public static string UserEmailIndex  => Prefix + "useremailindex";
    public static string RefreshTokens   => Prefix + "refreshtokens";
    public static string AuthTokens      => Prefix + "authtokens";
    public static string Events          => Prefix + "events";
    public static string EventAttendees  => Prefix + "eventattendees";
    public static string EventInterested  => Prefix + "eventinterested";
    public static string Likes           => Prefix + "likes";
    public static string LikesReceived   => Prefix + "likesreceived";
    public static string Matches         => Prefix + "matches";
    public static string BlogPosts       => Prefix + "blogposts";
    public static string StoreItems      => Prefix + "storeitems";
    public static string ForumSections   => Prefix + "forumsections";
    public static string ForumTopics     => Prefix + "forumtopics";
    public static string ForumTopicIndex => Prefix + "forumtopicindex";
    public static string ForumReplies    => Prefix + "forumreplies";
    public static string Chats           => Prefix + "chats";
    public static string UserChats       => Prefix + "userchats";
    public static string Messages        => Prefix + "messages";
    public static string AppConfig       => Prefix + "appconfig";
    public static string EventInvites    => Prefix + "eventinvites";
    public static string UserTelegramIndex => Prefix + "usertelegramindex";
}
