namespace Lovecraft.Backend.Services;

/// <summary>Thrown when an account name fails format or reserved-name validation.</summary>
public class InvalidAccountNameException : Exception
{
    /// <summary>"invalidFormat" or "reserved"</summary>
    public string Reason { get; }
    public InvalidAccountNameException(string reason) : base($"Invalid account name: {reason}")
    {
        Reason = reason;
    }
}
