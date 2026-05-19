namespace Lovecraft.NotificationsWorker;

/// <summary>
/// Worker-local copy of the notification-related table names.
/// Mirrors backend's <c>Lovecraft.Backend.Storage.TableNames</c> — keep in sync.
/// Respects the same <c>AZURE_TABLE_PREFIX</c> env var the backend uses.
/// </summary>
public static class TableNames
{
    public static string Prefix { get; set; } = Environment.GetEnvironmentVariable("AZURE_TABLE_PREFIX") ?? string.Empty;

    public static string Notifications           => Prefix + "notifications";
    public static string NotificationsOutbox     => Prefix + "notificationsoutbox";
    public static string NotificationPreferences => Prefix + "notificationpreferences";
    public static string Users                   => Prefix + "users";
    public static string Events                  => Prefix + "events";
    public static string EventAttendees          => Prefix + "eventattendees";
}
