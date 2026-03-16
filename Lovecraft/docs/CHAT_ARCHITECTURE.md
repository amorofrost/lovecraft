# Chat Architecture

> Added March 15, 2026. Updated March 16, 2026. Covers both the REST chat API and the SignalR real-time layer.

---

## Overview

The chat system has two transport layers:

- **REST** (`/api/v1/chats`) — create chats, fetch history, send messages (fallback / initial load)
- **SignalR** (`/hubs/chat`) — real-time message delivery and forum reply notifications

The frontend uses the REST layer for initial data load (chat list, message history) and the SignalR layer for live updates. Both layers share the same `IChatService` implementation.

---

## SignalR Hub: `ChatHub`

**Location**: `Lovecraft.Backend/Hubs/ChatHub.cs`
**URL**: `/hubs/chat`
**Auth**: `[Authorize]` — JWT Bearer token passed as `?access_token=<token>` query string (required because WebSocket headers cannot carry `Authorization`)

### Hub Groups

| Group name | Joined by | Used for |
|---|---|---|
| `chat-{chatId}` | `JoinChat(chatId)` | Private 1-on-1 chat rooms |
| `topic-{topicId}` | `JoinTopic(topicId)` | Forum topic discussion rooms |

### Client → Server methods

| Method | Parameters | Notes |
|---|---|---|
| `JoinChat` | `chatId: string` | Validates that the calling user is a participant via `IChatService.ValidateAccessAsync` |
| `JoinTopic` | `topicId: string` | No access check — forum topics are public |
| `LeaveGroup` | `groupId: string` | Removes caller from any hub group |
| `SendMessage` | `chatId, content: string` | Validates access, persists via `IChatService.SendMessageAsync`, broadcasts `MessageReceived` to `Clients.OthersInGroup` (sender excluded from hub echo) |

### Server → Client events

| Event | Payload | Emitted by |
|---|---|---|
| `MessageReceived` | `MessageDto` | `ChatsController.SendMessage` via `IHubContext<ChatHub>.Clients.Group($"chat-{id}")` (REST path — all group members including sender receive it); also by `ChatHub.SendMessage` via `Clients.OthersInGroup` (hub path — sender excluded) |
| `ReplyPosted` | `(ForumReplyDto reply, string topicId)` | `ForumController.CreateReply` (via `IHubContext<ChatHub>`) |

> **Message delivery flow (REST path, which the frontend uses):** Client sends `POST /api/v1/chats/{id}/messages` → controller persists via `IChatService` → broadcasts `MessageReceived` to the full group via `IHubContext`. The sender receives the event too; the frontend deduplicates by message ID to prevent double-display.

---

## REST Endpoints

All require `Authorization: Bearer <token>`.

| Method | URL | Body | Description |
|---|---|---|---|
| `GET` | `/api/v1/chats` | — | List user's chats (filtered by participant) |
| `POST` | `/api/v1/chats` | `{ targetUserId }` | Get existing or create new private chat |
| `GET` | `/api/v1/chats/{id}/messages` | — | Paginated messages (`?page=1&pageSize=50`) |
| `POST` | `/api/v1/chats/{id}/messages` | `{ content }` | Persist message and broadcast `MessageReceived` to all group members via `IHubContext<ChatHub>` |

---

## Azure Table Storage Schema

Three new tables (added to `TableNames.cs`):

### `chats` table

| Field | Value |
|---|---|
| PartitionKey | `"CHAT"` |
| RowKey | `chatId` |
| `ParticipantIds` | Comma-separated user IDs |
| `CreatedAt` | ISO 8601 timestamp |

**Entity**: `ChatEntity.cs`

### `userchats` table (index)

Mirrors the `LikesReceived` index pattern — one row per user per chat, for efficient per-user chat listing.

| Field | Value |
|---|---|
| PartitionKey | `userId` |
| RowKey | `chatId` |
| `OtherUserId` | The other participant's ID |
| `LastMessageContent` | Truncated last message |
| `LastMessageAt` | Timestamp of last message |
| `UnreadCount` | Unread message count |
| `UpdatedAt` | Updated on each new message |

**Entity**: `UserChatEntity.cs`

### `messages` table

| Field | Value |
|---|---|
| PartitionKey | `chatId` |
| RowKey | `{invertedTicks}_{messageId}` — newest messages sort first in Table Storage range scans |
| `MessageId` | Stable GUID identifier |
| `SenderId` | User ID |
| `Content` | Message text |
| `SentAt` | Timestamp |
| `Type` | `MessageType` enum (`text`, `image`, `system`) |
| `Read` | Boolean |

**Entity**: `MessageEntity.cs`

---

## Mock Mode

In mock mode (`USE_AZURE_STORAGE=false`), all data lives in `MockDataStore` static fields:

- `MockDataStore.Chats` — `List<ChatDto>`
- `MockDataStore.UserChats` — `Dictionary<string, List<(ChatId, OtherUserId, LastMsg, UpdatedAt)>>`
- `MockDataStore.Messages` — `Dictionary<string, List<MessageDto>>`

`MockChatService` implements `IChatService` against these collections.

SignalR still runs in mock mode (hub is registered). The frontend's `chatConnection.ts` is a no-op in mock mode (`isApiMode()` guard), so SignalR connections are never attempted on the client when running against the mock backend.

---

## JWT Authentication for SignalR

Standard WebSocket connections cannot send HTTP headers after the initial handshake. SignalR clients pass the JWT as a query string parameter:

```
ws://host/hubs/chat?access_token=<jwt>
```

On the server, `JwtBearerEvents.OnMessageReceived` in `Program.cs` reads the token from the query string and sets it as the Bearer token before authentication runs:

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = ctx =>
    {
        var token = ctx.Request.Query["access_token"];
        var path = ctx.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
            ctx.Token = token;
        return Task.CompletedTask;
    }
};
```

---

## nginx WebSocket Config

The `nginx.conf` in the frontend repository has a `/hubs/` location block that proxies WebSocket upgrade headers to the backend:

```nginx
location /hubs/ {
    proxy_pass         http://backend:8080;
    proxy_http_version 1.1;
    proxy_set_header   Upgrade $http_upgrade;
    proxy_set_header   Connection "upgrade";
    proxy_set_header   Host $host;
    proxy_cache_bypass $http_upgrade;
}
```

This must appear **before** any catch-all location block in the nginx config.

---

## Frontend Integration

See `src/services/signalr/chatConnection.ts` and `src/hooks/useChatSignalR.ts` in the frontend repo.

- `chatConnection` — module-level singleton; no-op in mock mode
- `useChatSignalR(type, id)` — React hook that joins a hub group on mount and leaves on unmount
- `onEvent(event, handler)` — returns a cleanup function; use as `return onEvent(...)` inside `useEffect`

---

## Auto-Creation on Mutual Like

When two users like each other, a 1-on-1 chat is created automatically — the user does not need to tap "Start chat" first. Both `MockMatchingService` and `AzureMatchingService` call `IChatService.GetOrCreateChatAsync(fromUserId, toUserId)` inside `CreateLikeAsync` when a mutual match is detected.

`GetOrCreateChatAsync` is idempotent — calling it multiple times for the same pair returns the same chat ID, so duplicate chats are never created.

---

## Event Discussion (Forum → Chat bridge)

Each event has an optional `forumTopicId` field on `EventDto`. When `GET /api/v1/events/{id}` is called and the event has no `forumTopicId`, the controller lazily creates a forum topic via `IForumService.CreateEventTopicAsync` and stores the ID via `IEventService.SetForumTopicIdAsync`. Subsequent calls return the same topic ID.

The `Talks.tsx` page treats this topic as the event's group discussion channel, loading replies via `forumsApi.getReplies(topicId)` and listening for `ReplyPosted` events via `useChatSignalR('topic', topicId)`.
