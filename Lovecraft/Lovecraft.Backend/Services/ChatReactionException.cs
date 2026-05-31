namespace Lovecraft.Backend.Services;

/// <summary>
/// Thrown from <see cref="IChatService"/> reaction methods. The <see cref="Code"/>
/// is the stable error code the controller surfaces to clients as an
/// <c>ApiResponse.error.code</c>.
/// </summary>
public class ChatReactionException : Exception
{
    public string Code { get; }
    public ChatReactionException(string code, string message) : base(message) { Code = code; }
}
