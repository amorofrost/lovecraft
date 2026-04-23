namespace Lovecraft.Backend.Configuration;

public class GoogleAuthOptions
{
    public const string SectionName = "Google";

    /// <summary>OAuth 2.0 Web client ID (xxx.apps.googleusercontent.com). Used to validate ID tokens with Google.</summary>
    public string ClientId { get; set; } = string.Empty;
}
