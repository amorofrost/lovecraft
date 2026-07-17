# Localize Telegram Bot Messages (ru/en)

**Date**: 2026-07-17
**Status**: Approved (design)
**Repo**: `lovecraft` (.NET) — `Lovecraft.Common`, `Lovecraft.NotificationsWorker`, `Lovecraft.TelegramBot`

---

## Problem

Every user-facing string the Telegram bot emits is hard-coded in English, but the
real user base uses the Russian app localization. Affected surfaces:

- **Notification bodies** — `TelegramMessageRenderer` (`Lovecraft.NotificationsWorker`), a
  `switch` over 9 notification types, all English.
- **Inline button labels** — `Open in app`, `Mute these` (same renderer).
- **Bot command / callback text** — `TelegramBotWorker` (`Lovecraft.TelegramBot`): the mute
  callback popup `Notifications muted`, the `/start` welcome, and the `/help` text.

There is no localization infrastructure in the backend today (the codebase is deliberately
framework-light — hand-rolled mappers, no i18n framework).

**Goal**: render all bot-emitted text in the recipient's language (Russian or English),
defaulting to Russian.

---

## Language sources (two, inherent)

The two emitting processes have different information available, so they resolve language
differently:

- **NotificationsWorker** sends **proactively** (no incoming Telegram update). It uses the
  user's **stored app language** from `UserEntity.SettingsJson`. That JSON is written by
  `AzureUserService` via `JsonSerializer.Serialize(dto.Settings)` with **default** options,
  so `Language` is stored as a **PascalCase, numeric** field: `{"...","Language":0,...}`
  (0 = Ru, 1 = En). Resolution is tolerant of numeric and string forms; default **Ru**.
- **TelegramBot worker** reacts to a **live update** (and the sender may be an unregistered
  user), so it uses the Telegram **client** language `Update…From.LanguageCode`. Default
  **Ru** for unknown codes.

Both default to Russian when unknown — matching the app default (`UserSettingsDto.Language =
Language.Ru`) and the real user base.

---

## Approach: hand-rolled shared localizer in `Lovecraft.Common`

A small compiled string table plus a language resolver, shared by both workers. No new
dependencies. `Lovecraft.NotificationsWorker` already references `Lovecraft.Common`;
`Lovecraft.TelegramBot` does **not** — this design **adds** that project reference.

### New: `Lovecraft.Common/Localization/TelegramStrings.cs`

```csharp
public static class TelegramStrings
{
    // Key constants (referenced from renderer + bot worker — prevents typos).
    public const string LikeReceived        = "tg.likeReceived";
    public const string MatchCreated        = "tg.matchCreated";
    public const string MessageReceived     = "tg.messageReceived";
    public const string ForumReply          = "tg.forumReply";
    public const string EventPublished      = "tg.eventPublished";
    public const string EventReminder       = "tg.eventReminder";
    public const string EventInvite         = "tg.eventInvite";
    public const string RankUp              = "tg.rankUp";
    public const string DefaultNotification = "tg.default";
    public const string BtnOpenInApp        = "tg.btn.openInApp";
    public const string BtnMute             = "tg.btn.mute";
    public const string BotStart            = "bot.start";
    public const string BotHelp             = "bot.help";
    public const string BotMuteAck          = "bot.muteAck";

    private static readonly Dictionary<string, (string Ru, string En)> Table = new()
    {
        [LikeReceived]        = ("❤️ Кто-то оценил ваш профиль", "❤️ Someone liked your profile"),
        [MatchCreated]        = ("💞 У вас новый мэтч!", "💞 You have a new match!"),
        [MessageReceived]     = ("💬 Новое сообщение: {0}", "💬 New message: {0}"),
        [ForumReply]          = ("💭 Кто-то ответил в теме", "💭 Someone replied in a thread"),
        [EventPublished]      = ("📅 Новое событие: <b>{0}</b>", "📅 New event: <b>{0}</b>"),
        [EventReminder]       = ("⏰ Событие завтра: <b>{0}</b>", "⏰ Event tomorrow: <b>{0}</b>"),
        [EventInvite]         = ("🎟️ Вас пригласили: <b>{0}</b>", "🎟️ You're invited: <b>{0}</b>"),
        [RankUp]              = ("🏆 Ваш новый статус — <b>{0}</b>!", "🏆 You're now <b>{0}</b>!"),
        [DefaultNotification] = ("У вас новое уведомление", "You have a new notification"),
        [BtnOpenInApp]        = ("Открыть в приложении", "Open in app"),
        [BtnMute]             = ("Отключить эти", "Mute these"),
        [BotStart]            = ("AloeVera Harmony Meet — нажмите кнопку меню, чтобы открыть мини-приложение, или войдите на сайте через Telegram.",
                                 "AloeVera Harmony Meet — use the menu button to open the mini app, or sign in on the website with Telegram."),
        [BotHelp]             = ("Команды: /start — приветствие. Откройте мини-приложение из меню бота для веб-версии внутри Telegram.",
                                 "Commands: /start — welcome. Open the Mini App from the bot menu for the web experience inside Telegram."),
        [BotMuteAck]          = ("Уведомления отключены", "Notifications muted"),
    };

    // Rank display names (mirror the frontend rank.* keys). Keyed by the camelCase enum
    // string carried in the RankUp payload's newRank field.
    private static readonly Dictionary<string, (string Ru, string En)> Ranks = new()
    {
        ["novice"]       = ("Новичок", "Novice"),
        ["activeMember"] = ("Активный участник", "Active Member"),
        ["friendOfAloe"] = ("Друг AloeVera", "Friend of Aloe"),
        ["aloeCrew"]     = ("Команда AloeVera", "Aloe Crew"),
    };

    /// <summary>Localized template for a known key. Throws if the key is absent (a bug —
    /// every key is a compile-time constant covered by TelegramStringsTests).</summary>
    public static string Get(Language lang, string key)
    {
        if (!Table.TryGetValue(key, out var pair))
            throw new KeyNotFoundException($"Missing Telegram string key: {key}");
        return lang == Language.En ? pair.En : pair.Ru;
    }

    /// <summary>Localized rank display name; falls back to the raw value for unknown ranks
    /// (never throws — rank strings originate from payload data).</summary>
    public static string GetRankName(Language lang, string rankValue)
    {
        if (Ranks.TryGetValue(rankValue, out var pair))
            return lang == Language.En ? pair.En : pair.Ru;
        return rankValue;
    }
}
```

### New: `Lovecraft.Common/Localization/LanguageResolver.cs`

```csharp
public static class LanguageResolver
{
    /// <summary>Telegram client language code (e.g. "ru", "en-US") → Language. Unknown → Ru.</summary>
    public static Language FromTelegramCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return Language.Ru;
        if (code.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return Language.En;
        return Language.Ru;   // "ru*" and every other/unknown code → app default
    }

    /// <summary>Parse the user's UserEntity.SettingsJson and return their Language.
    /// Tolerant of the stored numeric enum (0=Ru,1=En) and string forms; missing/malformed → Ru.
    /// Checks both "Language" (canonical PascalCase) and "language" (defensive).</summary>
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
        catch (JsonException) { return Language.Ru; }
    }
}
```

---

## NotificationsWorker changes

### `Entities/UserContactEntity.cs`
Add one column (already reading this table — no extra round-trip):

```csharp
/// <summary>Raw JSON of the user's settings (contains their Language). Parsed via LanguageResolver.</summary>
public string SettingsJson { get; set; } = "{}";
```

### `Renderers/ITelegramMessageRenderer.cs` + `TelegramMessageRenderer.cs`
Change the signature to accept the recipient's language:

```csharp
(string Html, InlineKeyboardMarkup Keyboard) Render(NotificationModel notification, Language language);
```

The body `switch` and both button labels source their text from `TelegramStrings`:

- Each fixed body → `TelegramStrings.Get(language, TelegramStrings.<Key>)`, with dynamic
  parts filled by `string.Format(...)` (e.g. `MessageReceived` →
  `string.Format(TelegramStrings.Get(language, TelegramStrings.MessageReceived), HttpUtility.HtmlEncode(preview))`).
- `RankUp` → `string.Format(Get(language, RankUp), HttpUtility.HtmlEncode(GetRankName(language, newRank)))`.
- `CommunityBroadcast` stays **unchanged** — its template `📣 <b>{0}</b>\n\n{1}` has no
  localizable words; `title`/`body` are admin-authored content.
- Buttons: `WithWebApp(TelegramStrings.Get(language, BtnOpenInApp), …)` and
  `WithCallbackData(TelegramStrings.Get(language, BtnMute), muteData)`. The `dest` URL and the
  `mute:{type}` callback **data** are unchanged (data, not label).

HTML-encoding of all dynamic content stays exactly where it is today.

### `Dispatchers/TelegramDispatcher.cs`
After fetching the `UserContactEntity` (Step 1), resolve the language and pass it to the
renderer:

```csharp
var language = LanguageResolver.FromSettings(entity.Value.SettingsJson);
...
var (html, keyboard) = _renderer.Render(notification, language);
```

---

## TelegramBot worker changes

### `Lovecraft.TelegramBot/Lovecraft.TelegramBot.csproj`
Add: `<ProjectReference Include="..\Lovecraft.Common\Lovecraft.Common.csproj" />`.

### `TelegramBotWorker.cs`
Resolve language from the live update's sender and localize the three strings:

- Mute callback: `var lang = LanguageResolver.FromTelegramCode(cb.From.LanguageCode);`
  → `AnswerCallbackQuery(cb.Id, TelegramStrings.Get(lang, TelegramStrings.BotMuteAck), …)`.
- `/start`: `var lang = LanguageResolver.FromTelegramCode(message.From?.LanguageCode);`
  → `SendMessage(chat, TelegramStrings.Get(lang, TelegramStrings.BotStart), …)`.
- `/help`: same `lang` → `TelegramStrings.Get(lang, TelegramStrings.BotHelp)`.

---

## What is localized vs. passed through

| Category | Localized? |
|---|---|
| Notification body templates (9 types, minus broadcast) | ✅ |
| Inline button labels (Open in app / Mute these) | ✅ |
| RankUp rank display name | ✅ (mirrors frontend `rank.*`) |
| `/start`, `/help`, mute-ack popup | ✅ |
| Message preview text, event titles | ❌ passed through (user content) |
| `CommunityBroadcast` title + body | ❌ passed through (admin content) |
| `mute:{type}` callback data, `dest` URLs | ❌ unchanged (not user-visible text) |

---

## Testing

**New** (`Lovecraft.UnitTests/Localization/`):
- `LanguageResolverTests` — `FromTelegramCode`: `"ru"`/`"ru-RU"`→Ru, `"en"`/`"en-US"`→En,
  `"de"`/`null`/`""`→Ru. `FromSettings`: `{"Language":0}`→Ru, `{"Language":1}`→En,
  `{"language":1}`→En, `{"Language":"en"}`→En, `{}`/`null`/`"garbage"`/`{"Language":"xx"}`→Ru.
- `TelegramStringsTests` — every `TelegramStrings` key constant resolves for both Ru and En
  (non-empty, distinct where expected); `Get` throws `KeyNotFoundException` for an unknown key;
  `GetRankName` returns localized names for the 4 ranks and falls back to the raw value for an
  unknown rank.

**Update** `Lovecraft.UnitTests/NotificationsWorker/TelegramMessageRendererTests.cs`:
- All `Render(notif)` call sites → `Render(notif, Language.Ru)` / `Language.En`.
- Assert Russian body substrings under `Language.Ru` and English under `Language.En` for
  representative types (MessageReceived, MatchCreated, EventReminder, RankUp-with-localized-rank).
- **Button location refactor**: existing tests find the open/mute buttons via English label
  text (`b.Text.Contains("Open")`, `Text.Contains("Open")`), which breaks under localization.
  Locate structurally instead — open button = `b.WebApp is not null`; mute button =
  `b.CallbackData?.StartsWith("mute:") == true`. The `dest`-decode assertions and
  `mute:{type}` data assertions are unchanged.

**Update** `Lovecraft.UnitTests/NotificationsWorker/TelegramDispatcherTests.cs`:
- This suite uses a **real** `TelegramMessageRenderer` (not a mock) and builds
  `new UserContactEntity { TelegramUserId = … }` with no `SettingsJson` (→ default `"{}"` →
  Ru). No mock-renderer setup exists to change. Existing assertions are on `DispatchResult` /
  send success, so they keep passing under the default-Ru path — confirm none assert on English
  body text; if any do, update the expected substring to Russian.
- Add a language-path test: capture the `html` argument passed to
  `ITelegramSendClient.SendAsync` (via `Mock.Callback`), build the entity with
  `SettingsJson = "{\"Language\":1}"`, and assert the captured HTML contains the **English**
  body; a sibling case with `SettingsJson = "{\"Language\":0}"` (or absent) asserts the
  **Russian** body. This verifies `LanguageResolver.FromSettings` → renderer end-to-end through
  the dispatcher.

**Bot worker**: the change swaps literals for `TelegramStrings.Get(LanguageResolver.From…)`;
correctness rests on the unit-tested resolver + string table. Verified by `dotnet build` and
the existing `Lovecraft.TelegramBot`-touching tests remaining green. No new bot-worker test is
added (the polling loop has no existing unit harness; extracting one is out of scope).

---

## Out of scope

- Email digest localization (`EmailDigestRenderer` — separate renderer; this work is scoped
  to the Telegram bot).
- Web / in-app notifications (already localized on the frontend).
- Per-notification language overrides, additional languages beyond ru/en.
