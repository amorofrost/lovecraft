namespace Lovecraft.Backend.Constants;

/// <summary>
/// Ordered list of valid prompt IDs. Must mirror src/data/prompts.ts in the
/// frontend repo. Adding/removing an ID requires a coordinated release.
/// </summary>
public static class PromptIds
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        "aloevera_first",
        "aloevera_song",
        "concert_memory",
        "looking_for",
        "playlist",
        "instrument"
    };
}
