using System;
using System.Text.Json;
using Lovecraft.Common.Enums;

namespace Lovecraft.Common.Localization;

/// <summary>
/// Resolves the notification/UI language for a Telegram recipient. Two sources:
/// the user's stored app setting (SettingsJson) for proactive sends, and the Telegram
/// client language code for reactive bot interactions. Unknown/malformed → Ru (app default).
/// </summary>
public static class LanguageResolver
{
    /// <summary>Telegram client language code (e.g. "ru", "en-US") → Language. Unknown → Ru.</summary>
    public static Language FromTelegramCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return Language.Ru;
        if (code.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return Language.En;
        return Language.Ru; // "ru*" and every other/unknown code → app default
    }

    /// <summary>
    /// Parse UserEntity.SettingsJson and return the user's Language. Tolerant of the stored
    /// numeric enum (0=Ru, 1=En, written by JsonSerializer with default options) and string
    /// forms; checks both "Language" (canonical PascalCase) and "language". Missing/malformed → Ru.
    /// </summary>
    public static Language FromSettings(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return Language.Ru;
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Language.Ru;
            if (!root.TryGetProperty("Language", out var el) &&
                !root.TryGetProperty("language", out el))
                return Language.Ru;

            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
                return n == 1 ? Language.En : Language.Ru;
            if (el.ValueKind == JsonValueKind.String &&
                string.Equals(el.GetString(), "en", StringComparison.OrdinalIgnoreCase))
                return Language.En;
            return Language.Ru;
        }
        catch (JsonException)
        {
            return Language.Ru;
        }
    }
}
