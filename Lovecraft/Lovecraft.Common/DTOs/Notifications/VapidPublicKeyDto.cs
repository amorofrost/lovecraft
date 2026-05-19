namespace Lovecraft.Common.DTOs.Notifications;

public class VapidPublicKeyDto
{
    /// <summary>Base64URL-encoded P-256 public key. Frontend uses this as applicationServerKey when subscribing.</summary>
    public string PublicKey { get; set; } = string.Empty;
}
