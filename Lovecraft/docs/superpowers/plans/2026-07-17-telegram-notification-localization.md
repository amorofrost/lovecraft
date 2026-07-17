# Telegram Bot Message Localization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render all Telegram-bot-emitted text (notification bodies, inline button labels, `/start`, `/help`, mute popup) in the recipient's language — Russian or English — defaulting to Russian.

**Architecture:** A hand-rolled shared localizer (`TelegramStrings` + `LanguageResolver`) in `Lovecraft.Common`. The NotificationsWorker resolves language from the user's stored `SettingsJson` and passes it to the message renderer; the TelegramBot worker resolves it from the live update's `From.LanguageCode`. No i18n framework, no new dependencies.

**Tech Stack:** C# / .NET 10, xUnit + Moq. `Telegram.Bot` 22.4.4.

## Global Constraints

- Repo root: `/home/amorofrost/src/lovecraft`. Solution + projects under `/home/amorofrost/src/lovecraft/Lovecraft`. Run `dotnet` from `/home/amorofrost/src/lovecraft/Lovecraft`. Work on branch `feature/telegram-notification-localization` (already created); commit there, never on `main`.
- `Language` enum is `Lovecraft.Common.Enums.Language` with values `Ru`, `En` (default `Ru`). The new localizer types live in namespace `Lovecraft.Common.Localization`.
- Default language is **Ru** everywhere language is unknown/unparseable.
- Russian and English strings must be reproduced **verbatim** from the tables in this plan (they come from the approved spec). Do not paraphrase.
- Do NOT change: the `CommunityBroadcast` body template (`📣 <b>{0}</b>\n\n{1}`), any `dest` URL, or the `mute:{type}` callback **data**. Only user-visible **text** is localized.
- `Lovecraft.UnitTests` already references `Lovecraft.Common`, `Lovecraft.NotificationsWorker`, and `Lovecraft.TelegramBot`. New test files under `Lovecraft.UnitTests/` are auto-included (glob).
- Tests run with `dotnet test` from `/home/amorofrost/src/lovecraft/Lovecraft`. Filter a class with `--filter "FullyQualifiedName~ClassName"`.
- Commit message body ends with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

---

## Task 1: Shared localizer in `Lovecraft.Common` (`TelegramStrings` + `LanguageResolver`)

**Files:**
- Create: `Lovecraft/Lovecraft.Common/Localization/TelegramStrings.cs`
- Create: `Lovecraft/Lovecraft.Common/Localization/LanguageResolver.cs`
- Test: `Lovecraft/Lovecraft.UnitTests/Localization/TelegramStringsTests.cs`
- Test: `Lovecraft/Lovecraft.UnitTests/Localization/LanguageResolverTests.cs`

**Interfaces:**
- Consumes: `Lovecraft.Common.Enums.Language`.
- Produces (used by Tasks 2 and 3):
  - `TelegramStrings.Get(Language lang, string key) : string` — localized template; throws `KeyNotFoundException` for unknown keys.
  - `TelegramStrings.GetRankName(Language lang, string rankValue) : string` — localized rank name; returns `rankValue` unchanged for unknown ranks.
  - `public const string` keys: `LikeReceived, MatchCreated, MessageReceived, ForumReply, EventPublished, EventReminder, EventInvite, RankUp, DefaultNotification, BtnOpenInApp, BtnMute, BotStart, BotHelp, BotMuteAck`.
  - `LanguageResolver.FromTelegramCode(string? code) : Language` and `LanguageResolver.FromSettings(string? settingsJson) : Language`.

- [ ] **Step 1: Write the failing tests**

Create `Lovecraft/Lovecraft.UnitTests/Localization/LanguageResolverTests.cs`:

```csharp
using Lovecraft.Common.Enums;
using Lovecraft.Common.Localization;
using Xunit;

namespace Lovecraft.UnitTests.Localization;

public class LanguageResolverTests
{
    [Theory]
    [InlineData("ru", Language.Ru)]
    [InlineData("ru-RU", Language.Ru)]
    [InlineData("en", Language.En)]
    [InlineData("en-US", Language.En)]
    [InlineData("EN", Language.En)]
    [InlineData("de", Language.Ru)]
    [InlineData("", Language.Ru)]
    [InlineData(null, Language.Ru)]
    public void FromTelegramCode_maps_expected(string? code, Language expected)
    {
        Assert.Equal(expected, LanguageResolver.FromTelegramCode(code));
    }

    [Theory]
    [InlineData("{\"Language\":0}", Language.Ru)]
    [InlineData("{\"Language\":1}", Language.En)]
    [InlineData("{\"language\":1}", Language.En)]
    [InlineData("{\"Language\":\"en\"}", Language.En)]
    [InlineData("{\"Language\":\"En\"}", Language.En)]
    [InlineData("{\"Language\":\"ru\"}", Language.Ru)]
    [InlineData("{\"Language\":\"xx\"}", Language.Ru)]
    [InlineData("{\"Language\":2}", Language.Ru)]
    [InlineData("{}", Language.Ru)]
    [InlineData("", Language.Ru)]
    [InlineData(null, Language.Ru)]
    [InlineData("not-json", Language.Ru)]
    [InlineData("[1,2,3]", Language.Ru)]
    public void FromSettings_maps_expected(string? json, Language expected)
    {
        Assert.Equal(expected, LanguageResolver.FromSettings(json));
    }
}
```

Create `Lovecraft/Lovecraft.UnitTests/Localization/TelegramStringsTests.cs`:

```csharp
using System.Collections.Generic;
using Lovecraft.Common.Enums;
using Lovecraft.Common.Localization;
using Xunit;

namespace Lovecraft.UnitTests.Localization;

public class TelegramStringsTests
{
    public static IEnumerable<object[]> AllKeys => new[]
    {
        new object[] { TelegramStrings.LikeReceived },
        new object[] { TelegramStrings.MatchCreated },
        new object[] { TelegramStrings.MessageReceived },
        new object[] { TelegramStrings.ForumReply },
        new object[] { TelegramStrings.EventPublished },
        new object[] { TelegramStrings.EventReminder },
        new object[] { TelegramStrings.EventInvite },
        new object[] { TelegramStrings.RankUp },
        new object[] { TelegramStrings.DefaultNotification },
        new object[] { TelegramStrings.BtnOpenInApp },
        new object[] { TelegramStrings.BtnMute },
        new object[] { TelegramStrings.BotStart },
        new object[] { TelegramStrings.BotHelp },
        new object[] { TelegramStrings.BotMuteAck },
    };

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void Every_key_has_both_languages(string key)
    {
        Assert.False(string.IsNullOrWhiteSpace(TelegramStrings.Get(Language.Ru, key)));
        Assert.False(string.IsNullOrWhiteSpace(TelegramStrings.Get(Language.En, key)));
    }

    [Fact]
    public void Get_throws_for_unknown_key()
    {
        Assert.Throws<KeyNotFoundException>(() => TelegramStrings.Get(Language.Ru, "tg.does.not.exist"));
    }

    [Theory]
    [InlineData("novice", "Новичок", "Novice")]
    [InlineData("activeMember", "Активный участник", "Active Member")]
    [InlineData("friendOfAloe", "Друг AloeVera", "Friend of Aloe")]
    [InlineData("aloeCrew", "Команда AloeVera", "Aloe Crew")]
    public void GetRankName_localizes_known_ranks(string rank, string ru, string en)
    {
        Assert.Equal(ru, TelegramStrings.GetRankName(Language.Ru, rank));
        Assert.Equal(en, TelegramStrings.GetRankName(Language.En, rank));
    }

    [Fact]
    public void GetRankName_falls_back_to_raw_for_unknown_rank()
    {
        Assert.Equal("mysteryRank", TelegramStrings.GetRankName(Language.Ru, "mysteryRank"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /home/amorofrost/src/lovecraft/Lovecraft && dotnet test --filter "FullyQualifiedName~Localization"`
Expected: FAIL — build error (`TelegramStrings` / `LanguageResolver` / namespace `Lovecraft.Common.Localization` do not exist yet).

- [ ] **Step 3: Create `LanguageResolver.cs`**

Create `Lovecraft/Lovecraft.Common/Localization/LanguageResolver.cs`:

```csharp
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
```

- [ ] **Step 4: Create `TelegramStrings.cs`**

Create `Lovecraft/Lovecraft.Common/Localization/TelegramStrings.cs`:

```csharp
using System.Collections.Generic;
using Lovecraft.Common.Enums;

namespace Lovecraft.Common.Localization;

/// <summary>
/// Compiled (Ru, En) string table for all Telegram-bot-emitted text. Dynamic parts use {0}
/// placeholders filled by string.Format at the call site.
/// </summary>
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

    // Rank display names (mirror the frontend rank.* keys), keyed by the camelCase enum string
    // carried in the RankUp payload's newRank field.
    private static readonly Dictionary<string, (string Ru, string En)> Ranks = new()
    {
        ["novice"]       = ("Новичок", "Novice"),
        ["activeMember"] = ("Активный участник", "Active Member"),
        ["friendOfAloe"] = ("Друг AloeVera", "Friend of Aloe"),
        ["aloeCrew"]     = ("Команда AloeVera", "Aloe Crew"),
    };

    /// <summary>Localized template for a known key. Throws if the key is absent (a bug — every
    /// key is a compile-time constant covered by TelegramStringsTests).</summary>
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

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd /home/amorofrost/src/lovecraft/Lovecraft && dotnet test --filter "FullyQualifiedName~Localization"`
Expected: PASS — all `LanguageResolverTests` + `TelegramStringsTests` green.

- [ ] **Step 6: Commit**

```bash
cd /home/amorofrost/src/lovecraft
git add Lovecraft/Lovecraft.Common/Localization/ Lovecraft/Lovecraft.UnitTests/Localization/
git commit -m "feat: shared Telegram string localizer in Lovecraft.Common

TelegramStrings (Ru/En table + rank names) and LanguageResolver
(Telegram code + SettingsJson → Language, default Ru), with unit tests.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Localize NotificationsWorker rendering

**Files:**
- Modify: `Lovecraft/Lovecraft.NotificationsWorker/Entities/UserContactEntity.cs`
- Modify: `Lovecraft/Lovecraft.NotificationsWorker/Renderers/ITelegramMessageRenderer.cs`
- Modify: `Lovecraft/Lovecraft.NotificationsWorker/Renderers/TelegramMessageRenderer.cs`
- Modify: `Lovecraft/Lovecraft.NotificationsWorker/Dispatchers/TelegramDispatcher.cs`
- Test: `Lovecraft/Lovecraft.UnitTests/NotificationsWorker/TelegramMessageRendererTests.cs` (full rewrite)
- Test: `Lovecraft/Lovecraft.UnitTests/NotificationsWorker/TelegramDispatcherTests.cs` (extend)

**Interfaces:**
- Consumes: `TelegramStrings.Get/GetRankName`, `LanguageResolver.FromSettings`, `Lovecraft.Common.Enums.Language` (Task 1).
- Produces: `ITelegramMessageRenderer.Render(NotificationModel, Language)` — the renderer now requires a language. `UserContactEntity.SettingsJson` string column. `TelegramDispatcher` resolves language from the entity and passes it.

- [ ] **Step 1: Rewrite `TelegramMessageRendererTests.cs` and extend `TelegramDispatcherTests.cs` (tests first)**

Replace the entire contents of `Lovecraft/Lovecraft.UnitTests/NotificationsWorker/TelegramMessageRendererTests.cs` with:

```csharp
using System;
using System.Linq;
using Lovecraft.Common.Enums;
using Lovecraft.NotificationsWorker.Models;
using Lovecraft.NotificationsWorker.Renderers;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;

namespace Lovecraft.UnitTests.NotificationsWorker;

public class TelegramMessageRendererTests
{
    private readonly TelegramMessageRenderer _renderer = new(NullLogger<TelegramMessageRenderer>.Instance);

    // Locates the "Open in app" web_app button structurally (its label is localized, so we
    // cannot match on text) and decodes its relative dest path.
    private static string DestOf(InlineKeyboardMarkup keyboard)
    {
        var open = keyboard.InlineKeyboard.SelectMany(row => row).First(b => b.WebApp is not null);
        Assert.Null(open.Url);
        var url = open.WebApp!.Url;
        Assert.StartsWith("https://aloeve.club/tg?dest=", url);
        const string marker = "?dest=";
        var encoded = url[(url.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        return Uri.UnescapeDataString(encoded);
    }

    private static Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton OpenButton(InlineKeyboardMarkup keyboard) =>
        keyboard.InlineKeyboard.SelectMany(row => row).First(b => b.WebApp is not null);

    private static Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton MuteButton(InlineKeyboardMarkup keyboard) =>
        keyboard.InlineKeyboard.SelectMany(row => row).First(b => b.CallbackData?.StartsWith("mute:") == true);

    // ---- Localized bodies ----

    [Fact]
    public void MessageReceived_body_is_russian_for_ru()
    {
        var notif = new NotificationModel("n2", "u1", "MessageReceived", "actor",
            "{\"chatId\":\"c1\",\"preview\":\"hello there\"}", DateTime.UtcNow);

        var (html, _) = _renderer.Render(notif, Language.Ru);

        Assert.Contains("Новое сообщение", html);
        Assert.Contains("hello there", html);   // preview content passes through untranslated
    }

    [Fact]
    public void MessageReceived_body_is_english_for_en()
    {
        var notif = new NotificationModel("n2", "u1", "MessageReceived", "actor",
            "{\"chatId\":\"c1\",\"preview\":\"hello there\"}", DateTime.UtcNow);

        var (html, _) = _renderer.Render(notif, Language.En);

        Assert.Contains("New message", html);
        Assert.Contains("hello there", html);
    }

    [Fact]
    public void MatchCreated_body_localized()
    {
        var notif = new NotificationModel("n1", "u1", "MatchCreated", "actor", "{}", DateTime.UtcNow);

        Assert.Contains("новый мэтч", _renderer.Render(notif, Language.Ru).Html);
        Assert.Contains("new match", _renderer.Render(notif, Language.En).Html);
    }

    [Fact]
    public void EventReminder_body_localized_title_passthrough()
    {
        var notif = new NotificationModel("n10", "u1", "EventReminder", null,
            "{\"eventId\":\"e1\",\"eventTitle\":\"Show\"}", DateTime.UtcNow);

        var ru = _renderer.Render(notif, Language.Ru).Html;
        Assert.Contains("Событие завтра", ru);
        Assert.Contains("Show", ru);            // event title passes through
        Assert.Contains("Event tomorrow", _renderer.Render(notif, Language.En).Html);
    }

    [Fact]
    public void RankUp_uses_localized_rank_name()
    {
        var notif = new NotificationModel("n12", "u1", "RankUp", null,
            "{\"newRank\":\"aloeCrew\"}", DateTime.UtcNow);

        Assert.Contains("Команда AloeVera", _renderer.Render(notif, Language.Ru).Html);
        Assert.Contains("Aloe Crew", _renderer.Render(notif, Language.En).Html);
    }

    [Fact]
    public void Default_notification_localized()
    {
        var notif = new NotificationModel("nz", "u1", "SomethingUnknown", null, "{}", DateTime.UtcNow);

        Assert.Contains("новое уведомление", _renderer.Render(notif, Language.Ru).Html);
        Assert.Contains("new notification", _renderer.Render(notif, Language.En).Html);
    }

    // ---- Buttons ----

    [Fact]
    public void Open_button_is_a_web_app_button_pointing_at_the_mini_app()
    {
        var notif = new NotificationModel("n3", "u1", "MatchCreated", "actor",
            "{\"matchId\":\"m1\"}", DateTime.UtcNow);

        var (_, keyboard) = _renderer.Render(notif, Language.Ru);

        Assert.NotNull(keyboard);
        var open = OpenButton(keyboard);
        Assert.Null(open.Url);
        Assert.StartsWith("https://aloeve.club/tg?dest=", open.WebApp!.Url);
    }

    [Fact]
    public void Open_button_label_is_localized()
    {
        var notif = new NotificationModel("n3", "u1", "MatchCreated", "actor", "{}", DateTime.UtcNow);

        Assert.Equal("Открыть в приложении", OpenButton(_renderer.Render(notif, Language.Ru).Keyboard).Text);
        Assert.Equal("Open in app", OpenButton(_renderer.Render(notif, Language.En).Keyboard).Text);
    }

    [Fact]
    public void Mute_button_data_stable_label_localized()
    {
        var notif = new NotificationModel("n4", "u1", "MessageReceived", "actor",
            "{\"chatId\":\"c1\"}", DateTime.UtcNow);

        var muteRu = MuteButton(_renderer.Render(notif, Language.Ru).Keyboard);
        var muteEn = MuteButton(_renderer.Render(notif, Language.En).Keyboard);
        Assert.Equal("mute:messageReceived", muteRu.CallbackData);   // data unchanged
        Assert.Equal("mute:messageReceived", muteEn.CallbackData);
        Assert.Equal("Отключить эти", muteRu.Text);
        Assert.Equal("Mute these", muteEn.Text);
    }

    // ---- dest paths (unchanged behavior; language-agnostic) ----

    [Fact]
    public void CommunityBroadcast_uses_payload_link()
    {
        var notif = new NotificationModel("n5", "u1", "CommunityBroadcast", null,
            "{\"title\":\"Big news\",\"body\":\"something\",\"link\":\"/aloevera/events/42\"}", DateTime.UtcNow);

        Assert.Equal("/aloevera/events/42", DestOf(_renderer.Render(notif, Language.Ru).Keyboard));
    }

    [Fact]
    public void CommunityBroadcast_body_passes_through_untranslated()
    {
        var notif = new NotificationModel("n5", "u1", "CommunityBroadcast", null,
            "{\"title\":\"Big news\",\"body\":\"something\"}", DateTime.UtcNow);

        var html = _renderer.Render(notif, Language.Ru).Html;
        Assert.Contains("Big news", html);
        Assert.Contains("something", html);
    }

    [Fact]
    public void CommunityBroadcast_disallows_off_domain_absolute_urls()
    {
        var notif = new NotificationModel("n7", "u1", "CommunityBroadcast", null,
            "{\"title\":\"X\",\"body\":\"Y\",\"link\":\"https://evil.example/phish\"}", DateTime.UtcNow);

        Assert.Equal("/aloevera", DestOf(_renderer.Render(notif, Language.Ru).Keyboard));
    }

    [Fact]
    public void MessageReceived_dest_points_to_chat()
    {
        var notif = new NotificationModel("n8", "u1", "MessageReceived", "actor",
            "{\"chatId\":\"c1\",\"preview\":\"hi\"}", DateTime.UtcNow);

        Assert.Equal("/talks?chat=c1", DestOf(_renderer.Render(notif, Language.Ru).Keyboard));
    }

    [Fact]
    public void ForumReply_dest_points_to_topic()
    {
        var notif = new NotificationModel("n9", "u1", "ForumReplyToThread", "actor",
            "{\"topicId\":\"t1\"}", DateTime.UtcNow);

        Assert.Equal("/talks?topic=t1", DestOf(_renderer.Render(notif, Language.Ru).Keyboard));
    }

    [Fact]
    public void EventReminder_dest_points_to_event()
    {
        var notif = new NotificationModel("n10", "u1", "EventReminder", null,
            "{\"eventId\":\"e1\",\"eventTitle\":\"Show\"}", DateTime.UtcNow);

        Assert.Equal("/aloevera/events/e1", DestOf(_renderer.Render(notif, Language.Ru).Keyboard));
    }

    [Fact]
    public void LikeReceived_dest_targets_actor_profile()
    {
        var notif = new NotificationModel("n11", "u1", "LikeReceived", "actor-9",
            "{\"likeId\":\"l1\"}", DateTime.UtcNow);

        Assert.Equal("/friends?userId=actor-9", DestOf(_renderer.Render(notif, Language.Ru).Keyboard));
    }

    [Fact]
    public void RankUp_dest_points_to_settings()
    {
        var notif = new NotificationModel("n12", "u1", "RankUp", null,
            "{\"newRank\":\"aloeCrew\"}", DateTime.UtcNow);

        Assert.Equal("/settings", DestOf(_renderer.Render(notif, Language.Ru).Keyboard));
    }

    [Fact]
    public void Malformed_payload_renders_gracefully()
    {
        var notif = new NotificationModel("n6", "u1", "MessageReceived", "actor",
            "not-valid-json", DateTime.UtcNow);

        var (html, keyboard) = _renderer.Render(notif, Language.Ru);

        Assert.NotNull(html);
        Assert.NotEmpty(html);
        Assert.NotNull(keyboard);
    }
}
```

In `Lovecraft/Lovecraft.UnitTests/NotificationsWorker/TelegramDispatcherTests.cs`, make three edits:

(a) Add `using Lovecraft.Common.Enums;` at the top (after the existing `using` lines).

(b) Add a `settingsJson` parameter to `BuildDispatcher` and set it on the fixture entity. Change the signature line:

```csharp
    private static (TelegramDispatcher, Mock<ITelegramSendClient>) BuildDispatcher(
        string? telegramUserId,
        Func<Task>? sendBehavior = null,
        Mock<ITelegramRateLimiter>? rateLimiter = null)
```

to:

```csharp
    private static (TelegramDispatcher, Mock<ITelegramSendClient>) BuildDispatcher(
        string? telegramUserId,
        Func<Task>? sendBehavior = null,
        Mock<ITelegramRateLimiter>? rateLimiter = null,
        string? settingsJson = null)
```

and change the entity construction line:

```csharp
                .ReturnsAsync(Response.FromValue(new UserContactEntity { TelegramUserId = telegramUserId }, new Mock<Response>().Object));
```

to:

```csharp
                .ReturnsAsync(Response.FromValue(
                    new UserContactEntity { TelegramUserId = telegramUserId, SettingsJson = settingsJson ?? "{}" },
                    new Mock<Response>().Object));
```

(c) Append this test (before the closing brace of the class):

```csharp
    [Fact]
    public async Task Renders_in_user_language_from_settings()
    {
        // English settings (Language:1) → English body
        var (dispEn, sendEn) = BuildDispatcher(telegramUserId: "555111", settingsJson: "{\"Language\":1}");
        await dispEn.DispatchAsync(SampleNotification("MatchCreated"), CancellationToken.None);
        sendEn.Verify(s => s.SendAsync(
            It.IsAny<string>(),
            It.Is<string>(h => h.Contains("new match")),
            It.IsAny<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Russian settings (Language:0) → Russian body
        var (dispRu, sendRu) = BuildDispatcher(telegramUserId: "555111", settingsJson: "{\"Language\":0}");
        await dispRu.DispatchAsync(SampleNotification("MatchCreated"), CancellationToken.None);
        sendRu.Verify(s => s.SendAsync(
            It.IsAny<string>(),
            It.Is<string>(h => h.Contains("новый мэтч")),
            It.IsAny<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /home/amorofrost/src/lovecraft/Lovecraft && dotnet test --filter "FullyQualifiedName~TelegramMessageRendererTests|FullyQualifiedName~TelegramDispatcherTests"`
Expected: FAIL — build error: `Render` takes only one argument, `UserContactEntity` has no `SettingsJson`, and `Language` is used before the renderer is updated.

- [ ] **Step 3: Add `SettingsJson` to `UserContactEntity`**

In `Lovecraft/Lovecraft.NotificationsWorker/Entities/UserContactEntity.cs`, add this property inside the class (after `EmailVerified`):

```csharp
    /// <summary>Raw JSON of the user's settings (contains their Language). Parsed via LanguageResolver.</summary>
    public string SettingsJson { get; set; } = "{}";
```

- [ ] **Step 4: Change the renderer interface + implementation**

In `Lovecraft/Lovecraft.NotificationsWorker/Renderers/ITelegramMessageRenderer.cs`, add `using Lovecraft.Common.Enums;` at the top and change the method signature to:

```csharp
    (string Html, InlineKeyboardMarkup Keyboard) Render(NotificationModel notification, Language language);
```

In `Lovecraft/Lovecraft.NotificationsWorker/Renderers/TelegramMessageRenderer.cs`:

(a) Add these usings (alongside the existing ones):

```csharp
using Lovecraft.Common.Enums;
using Lovecraft.Common.Localization;
```

(b) Change the method signature:

```csharp
    public (string Html, InlineKeyboardMarkup Keyboard) Render(NotificationModel notification)
```

to:

```csharp
    public (string Html, InlineKeyboardMarkup Keyboard) Render(NotificationModel notification, Language language)
```

(c) Replace the entire `string body = notification.Type switch { … };` block with:

```csharp
        string body = notification.Type switch
        {
            "LikeReceived"        => TelegramStrings.Get(language, TelegramStrings.LikeReceived),
            "MatchCreated"        => TelegramStrings.Get(language, TelegramStrings.MatchCreated),
            "MessageReceived"     => string.Format(TelegramStrings.Get(language, TelegramStrings.MessageReceived),
                                                    HttpUtility.HtmlEncode(GetString(payload, "preview"))),
            "ForumReplyToThread"  => TelegramStrings.Get(language, TelegramStrings.ForumReply),
            "CommunityBroadcast"  => $"📣 <b>{HttpUtility.HtmlEncode(GetString(payload, "title"))}</b>\n\n{HttpUtility.HtmlEncode(GetString(payload, "body"))}",
            "EventPublished"      => string.Format(TelegramStrings.Get(language, TelegramStrings.EventPublished),
                                                    HttpUtility.HtmlEncode(GetString(payload, "eventTitle"))),
            "EventReminder"       => string.Format(TelegramStrings.Get(language, TelegramStrings.EventReminder),
                                                    HttpUtility.HtmlEncode(GetString(payload, "eventTitle"))),
            "EventInviteReceived" => string.Format(TelegramStrings.Get(language, TelegramStrings.EventInvite),
                                                    HttpUtility.HtmlEncode(GetString(payload, "eventTitle"))),
            "RankUp"              => string.Format(TelegramStrings.Get(language, TelegramStrings.RankUp),
                                                    HttpUtility.HtmlEncode(TelegramStrings.GetRankName(language, GetString(payload, "newRank")))),
            _                     => TelegramStrings.Get(language, TelegramStrings.DefaultNotification),
        };
```

(d) Replace the two `InlineKeyboardButton` construction lines:

```csharp
                InlineKeyboardButton.WithWebApp("Open in app", new WebAppInfo { Url = webAppUrl }),
                InlineKeyboardButton.WithCallbackData("Mute these", muteData),
```

with:

```csharp
                InlineKeyboardButton.WithWebApp(TelegramStrings.Get(language, TelegramStrings.BtnOpenInApp), new WebAppInfo { Url = webAppUrl }),
                InlineKeyboardButton.WithCallbackData(TelegramStrings.Get(language, TelegramStrings.BtnMute), muteData),
```

(e) Delete the now-unused `IsAnonymous` helper method entirely (the `LikeReceived` branch no longer calls it):

```csharp
    private static bool IsAnonymous(Dictionary<string, object?> payload)
    {
        var v = GetString(payload, "anonymous");
        return v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("True", StringComparison.OrdinalIgnoreCase);
    }
```

Leave `BuildDestPath`, `ResolveCommunityBroadcastPath`, `GetString`, and `ToCamelCase` unchanged.

- [ ] **Step 5: Resolve + pass language in `TelegramDispatcher`**

In `Lovecraft/Lovecraft.NotificationsWorker/Dispatchers/TelegramDispatcher.cs`:

(a) Add usings:

```csharp
using Lovecraft.Common.Enums;
using Lovecraft.Common.Localization;
```

(b) The dispatcher currently discards the fetched entity after reading `TelegramUserId`. Capture the language from the same entity. Change:

```csharp
            var pk = UserContactEntity.GetPartitionKey(notification.UserId);
            var entity = await _users.GetEntityAsync<UserContactEntity>(pk, notification.UserId, cancellationToken: ct);
            telegramUserId = entity.Value.TelegramUserId;
```

to:

```csharp
            var pk = UserContactEntity.GetPartitionKey(notification.UserId);
            var entity = await _users.GetEntityAsync<UserContactEntity>(pk, notification.UserId, cancellationToken: ct);
            telegramUserId = entity.Value.TelegramUserId;
            language = LanguageResolver.FromSettings(entity.Value.SettingsJson);
```

(c) Declare `language` next to the existing `telegramUserId` declaration. Change:

```csharp
        string? telegramUserId = null;
```

to:

```csharp
        string? telegramUserId = null;
        Language language = Language.Ru;
```

(d) Change the render call:

```csharp
        var (html, keyboard) = _renderer.Render(notification);
```

to:

```csharp
        var (html, keyboard) = _renderer.Render(notification, language);
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd /home/amorofrost/src/lovecraft/Lovecraft && dotnet test --filter "FullyQualifiedName~TelegramMessageRendererTests|FullyQualifiedName~TelegramDispatcherTests"`
Expected: PASS — renderer + dispatcher tests all green.

- [ ] **Step 7: Commit**

```bash
cd /home/amorofrost/src/lovecraft
git add Lovecraft/Lovecraft.NotificationsWorker/ Lovecraft/Lovecraft.UnitTests/NotificationsWorker/
git commit -m "feat: localize Telegram notification bodies + buttons

Renderer takes the recipient Language and sources all text from
TelegramStrings; dispatcher resolves language from the user's SettingsJson.
Tests assert Ru/En bodies and localized button labels; dest paths and
mute callback data unchanged.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Localize the TelegramBot worker (`/start`, `/help`, mute popup)

**Files:**
- Modify: `Lovecraft/Lovecraft.TelegramBot/Lovecraft.TelegramBot.csproj`
- Modify: `Lovecraft/Lovecraft.TelegramBot/TelegramBotWorker.cs`

**Interfaces:**
- Consumes: `TelegramStrings.Get`, `LanguageResolver.FromTelegramCode` (Task 1).
- Produces: no new API. Behavior change only — the three bot strings render in the sender's Telegram client language.

- [ ] **Step 1: Add the `Lovecraft.Common` project reference**

In `Lovecraft/Lovecraft.TelegramBot/Lovecraft.TelegramBot.csproj`, add a project reference. Insert this `ItemGroup` after the existing `PackageReference` `ItemGroup`:

```xml
  <ItemGroup>
    <ProjectReference Include="..\Lovecraft.Common\Lovecraft.Common.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Verify the reference resolves (build)**

Run: `cd /home/amorofrost/src/lovecraft/Lovecraft && dotnet build Lovecraft.TelegramBot/Lovecraft.TelegramBot.csproj`
Expected: PASS — builds with the new reference (no code using it yet).

- [ ] **Step 3: Localize the three strings in `TelegramBotWorker.cs`**

In `Lovecraft/Lovecraft.TelegramBot/TelegramBotWorker.cs`:

(a) Add usings at the top (after the existing `using` lines):

```csharp
using Lovecraft.Common.Localization;
```

(b) Change the mute-callback answer. Replace:

```csharp
            if (handled)
            {
                await bot.AnswerCallbackQuery(cb.Id, "Notifications muted", cancellationToken: ct);
            }
```

with:

```csharp
            if (handled)
            {
                var lang = LanguageResolver.FromTelegramCode(cb.From.LanguageCode);
                await bot.AnswerCallbackQuery(cb.Id, TelegramStrings.Get(lang, TelegramStrings.BotMuteAck), cancellationToken: ct);
            }
```

(c) Change the `/start` handler. Replace:

```csharp
        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            await bot.SendMessage(
                message.Chat.Id,
                "AloeVera Harmony Meet — use the menu button to open the mini app, or sign in on the website with Telegram.",
                cancellationToken: ct);
            return;
        }
```

with:

```csharp
        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            var lang = LanguageResolver.FromTelegramCode(message.From?.LanguageCode);
            await bot.SendMessage(
                message.Chat.Id,
                TelegramStrings.Get(lang, TelegramStrings.BotStart),
                cancellationToken: ct);
            return;
        }
```

(d) Change the `/help` handler. Replace:

```csharp
        if (text.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
        {
            await bot.SendMessage(
                message.Chat.Id,
                "Commands: /start — welcome. Open the Mini App from the bot menu for the web experience inside Telegram.",
                cancellationToken: ct);
        }
```

with:

```csharp
        if (text.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
        {
            var lang = LanguageResolver.FromTelegramCode(message.From?.LanguageCode);
            await bot.SendMessage(
                message.Chat.Id,
                TelegramStrings.Get(lang, TelegramStrings.BotHelp),
                cancellationToken: ct);
        }
```

- [ ] **Step 4: Build the bot worker and run the full suite**

Run: `cd /home/amorofrost/src/lovecraft/Lovecraft && dotnet build Lovecraft.TelegramBot/Lovecraft.TelegramBot.csproj`
Expected: PASS — compiles with the localized strings.

Run: `cd /home/amorofrost/src/lovecraft/Lovecraft && dotnet test`
Expected: PASS — the whole `Lovecraft.UnitTests` suite is green (bot-worker correctness rests on the Task 1 unit-tested `TelegramStrings` + `LanguageResolver`; the worker's polling loop has no unit harness).

- [ ] **Step 5: Commit**

```bash
cd /home/amorofrost/src/lovecraft
git add Lovecraft/Lovecraft.TelegramBot/
git commit -m "feat: localize Telegram bot /start, /help, and mute popup

Add Lovecraft.Common reference; resolve language from the update's
From.LanguageCode and source the three strings from TelegramStrings.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: End-to-end verification

**Files:** none (verification only).

**Interfaces:** none.

- [ ] **Step 1: Full solution build (all projects compile with the new reference + signature)**

Run: `cd /home/amorofrost/src/lovecraft/Lovecraft && dotnet build`
Expected: PASS — every project builds, no warnings introduced by these changes (the removed `IsAnonymous` leaves no unused-symbol warning).

- [ ] **Step 2: Full test suite**

Run: `cd /home/amorofrost/src/lovecraft/Lovecraft && dotnet test`
Expected: PASS — entire `Lovecraft.UnitTests` suite green, including the new `Localization` tests and the updated renderer/dispatcher tests.

- [ ] **Step 3: Confirm no English notification literals remain in the renderer**

Run: `cd /home/amorofrost/src/lovecraft/Lovecraft && grep -nE "New message|new match|You're now|Open in app|Mute these|Someone liked|new notification" Lovecraft.NotificationsWorker/Renderers/TelegramMessageRenderer.cs Lovecraft.TelegramBot/TelegramBotWorker.cs`
Expected: **no matches** — every user-visible literal now comes from `TelegramStrings`. (Matches inside `Lovecraft.Common/Localization/TelegramStrings.cs` are expected and fine; that file is the source of the strings.)

---

## Self-Review

**Spec coverage:**
- Shared localizer (`TelegramStrings` + `LanguageResolver`) in `Lovecraft.Common` → Task 1. ✓
- Two language sources (SettingsJson for the worker, Telegram code for the bot) → Task 2 (dispatcher `FromSettings`) + Task 3 (`FromTelegramCode`). ✓
- Localized notification bodies (all types except broadcast), button labels, localized rank name → Task 2 Step 4. ✓
- `CommunityBroadcast` body + dest paths + `mute:{type}` data unchanged → Task 2 Step 4c/4d (broadcast line kept verbatim; mute data untouched) + renderer tests. ✓
- Bot `/start`, `/help`, mute popup localized; `Lovecraft.Common` reference added to TelegramBot → Task 3. ✓
- Tests: `LanguageResolverTests`, `TelegramStringsTests`, rewritten `TelegramMessageRendererTests` (structural button location + Ru/En body assertions), extended `TelegramDispatcherTests` (language path) → Tasks 1–2. ✓
- Default Ru everywhere → `LanguageResolver` defaults + dispatcher `Language language = Language.Ru` initializer. ✓
- Out of scope (email digest, web notifications) → untouched by every task. ✓

**Placeholder scan:** No TBD/TODO/"handle edge cases"/"similar to" — every code step shows complete code. ✓

**Type consistency:** `Render(NotificationModel, Language)` is defined in the interface (Task 2 Step 4), implemented in the renderer (Step 4b), and called by the dispatcher (Step 5d) and all tests (Step 1) with a `Language` argument. `TelegramStrings.Get/GetRankName` and `LanguageResolver.FromSettings/FromTelegramCode` signatures match between Task 1 definitions and Task 2/3 call sites. `UserContactEntity.SettingsJson` defined in Task 2 Step 3, read in Step 5b, set in the dispatcher-test fixture (Step 1b). ✓
