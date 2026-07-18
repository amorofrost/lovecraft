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
