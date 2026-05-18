namespace Lovecraft.Backend.Services.Notifications;

public interface IPresenceTracker
{
    void Join(string groupName, string userId);
    void Leave(string groupName, string userId);
    bool IsInGroup(string groupName, string userId);
}
