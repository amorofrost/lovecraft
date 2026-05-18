using System.Collections.Concurrent;

namespace Lovecraft.Backend.Services.Notifications;

/// <summary>
/// Tracks SignalR group membership in-memory with refcounting per (group, user).
/// One user can have multiple connections to the same group (multiple tabs);
/// they're only considered absent when all connections leave.
/// Registered as singleton so ChatHub + producers share state.
/// </summary>
public class PresenceTracker : IPresenceTracker
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _groups = new();

    public void Join(string groupName, string userId)
    {
        var users = _groups.GetOrAdd(groupName, _ => new ConcurrentDictionary<string, int>());
        users.AddOrUpdate(userId, 1, (_, count) => count + 1);
    }

    public void Leave(string groupName, string userId)
    {
        if (!_groups.TryGetValue(groupName, out var users)) return;
        users.AddOrUpdate(userId, 0, (_, count) => Math.Max(0, count - 1));
        if (users.TryGetValue(userId, out var c) && c == 0)
            users.TryRemove(userId, out _);
    }

    public bool IsInGroup(string groupName, string userId)
        => _groups.TryGetValue(groupName, out var users)
           && users.TryGetValue(userId, out var c)
           && c > 0;
}
