namespace Lovecraft.Common.Enums;

/// <summary>
/// Who can see an event in listings and full detail (see events-invites spec).
/// </summary>
public enum EventVisibility
{
    Public = 0,
    SecretHidden = 1,
    SecretTeaser = 2,
}
