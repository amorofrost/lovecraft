using Lovecraft.Common.DTOs.Notifications;

namespace Lovecraft.Backend.Services.Notifications;

/// <summary>
/// Resolves a <see cref="BroadcastAudienceDto"/> into the concrete recipient user IDs
/// for an admin broadcast. Implementations should silently return an empty list for
/// unknown audience types or invalid values rather than throwing.
/// </summary>
public interface IBroadcastAudienceResolver
{
    Task<IReadOnlyList<string>> ResolveAsync(BroadcastAudienceDto audience, CancellationToken ct);
}
