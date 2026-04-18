namespace Lovecraft.Backend.Services;

public class InviteRequiredException : Exception
{
    public InviteRequiredException() : base("Event invite code is required for registration") { }
}
