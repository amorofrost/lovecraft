namespace Lovecraft.Backend.Constants;

/// <summary>
/// Canonical set of emojis users may attach as message reactions. Must mirror
/// src/lib/reactions.ts in the frontend repo. Adding/removing an emoji requires
/// a coordinated release.
/// </summary>
public static class AllowedReactions
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "\U0001F44D", // 👍
        "❤️", // ❤️
        "\U0001F602", // 😂
        "\U0001F62E", // 😮
        "\U0001F622", // 😢
        "\U0001F64F", // 🙏
        "\U0001F525", // 🔥
        "\U0001F389", // 🎉
    };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);

    public static bool IsAllowed(string emoji) => Set.Contains(emoji);
}
