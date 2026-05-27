using System.Collections.Concurrent;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage.Entities;

namespace Lovecraft.Backend.Services.Caching;

/// <summary>
/// In-process index of "which events has each user attended", keyed by userId.
///
/// Replaces the cross-partition scan of <c>eventattendees</c> filtered by RowKey=userId
/// that previously dominated the cost of forum reply rendering, /users search, and
/// single-profile fetches. Values are immutable <see cref="IReadOnlyList{T}"/> snapshots
/// so callers can enumerate without coordinating with concurrent writers.
/// </summary>
public class AttendanceCache
{
    private static readonly IReadOnlyList<string> Empty = Array.Empty<string>();

    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _byUser = new();

    /// <summary>
    /// Eager-load every attendance row into the cache. Run at startup, before any
    /// reader can hit the service. One full table scan; grouped by userId in-process.
    /// </summary>
    public async Task LoadAsync(TableClient attendeesTable)
    {
        var staging = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await foreach (var row in attendeesTable.QueryAsync<EventAttendeeEntity>())
        {
            if (!staging.TryGetValue(row.RowKey, out var list))
            {
                list = new List<string>();
                staging[row.RowKey] = list;
            }
            list.Add(row.PartitionKey);
        }

        foreach (var kvp in staging)
            _byUser[kvp.Key] = kvp.Value;
    }

    /// <summary>Returns event IDs the user has attended. Empty list when unknown.</summary>
    public IReadOnlyList<string> GetForUser(string userId) =>
        _byUser.TryGetValue(userId, out var list) ? list : Empty;

    /// <summary>
    /// Record a new attendance. Idempotent — adding the same (user, event) twice is a no-op.
    /// </summary>
    public void AddAttendance(string userId, string eventId)
    {
        _byUser.AddOrUpdate(
            userId,
            addValueFactory: _ => new List<string> { eventId },
            updateValueFactory: (_, existing) =>
            {
                if (existing.Contains(eventId)) return existing;
                var next = new List<string>(existing.Count + 1);
                next.AddRange(existing);
                next.Add(eventId);
                return next;
            });
    }

    /// <summary>
    /// Remove an attendance. No-op when the user has no cached attendances or the event
    /// isn't in the list. Retry-loop guards against concurrent writers.
    /// </summary>
    public void RemoveAttendance(string userId, string eventId)
    {
        while (true)
        {
            if (!_byUser.TryGetValue(userId, out var existing) || !existing.Contains(eventId))
                return;

            var next = existing.Where(e => !e.Equals(eventId, StringComparison.Ordinal)).ToList();
            if (_byUser.TryUpdate(userId, next, existing))
                return;
        }
    }

    /// <summary>
    /// Remove an event from every user's attendance list. Called when an event is deleted.
    /// O(N_users) in-process fan-out; rare.
    /// </summary>
    public void RemoveEvent(string eventId)
    {
        foreach (var userId in _byUser.Keys.ToList())
            RemoveAttendance(userId, eventId);
    }

    /// <summary>Test/diagnostic helper — returns total number of cached users.</summary>
    public int UserCount => _byUser.Count;
}
