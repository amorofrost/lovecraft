namespace Lovecraft.Backend.Helpers;

public static class AppRuntime
{
    /// <summary>
    /// Set once during app startup (Program.cs). Used for infrastructure "app uptime".
    /// </summary>
    public static DateTime AppStartedAtUtc { get; set; }
}

