namespace Lovecraft.Backend.Services;

/// <summary>
/// Thrown from <see cref="IChatService"/> message-send paths for invariant
/// violations (e.g. invalid reply target). The <see cref="Code"/> is the stable
/// error code the controller surfaces to clients as an
/// <c>ApiResponse.error.code</c>.
/// </summary>
public class ChatMessageException : Exception
{
    public string Code { get; }
    public ChatMessageException(string code, string message) : base(message) { Code = code; }
}
