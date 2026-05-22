namespace Lovecraft.Backend.Services;

/// <summary>Thrown when registration loses a race to claim an account name.</summary>
public class AccountNameTakenException : Exception
{
    public AccountNameTakenException() : base("Account name is already taken.") { }
}
