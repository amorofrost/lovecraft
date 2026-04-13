namespace Lovecraft.Backend.Services;

public class InvalidInviteCodeException : Exception
{
    public InvalidInviteCodeException() : base("Invalid invite code") { }
}
