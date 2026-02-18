# Azure Storage Schema Design

**AloeVera Harmony Meet** - Data Storage Schema

**Storage Type**: Azure Table Storage + Azure Blob Storage  
**Last Updated**: February 17, 2026

---

## 📊 Overview

This document describes the data schema for Azure Storage (Tables and Blobs). Azure Table Storage is a NoSQL key-value store optimized for fast queries using PartitionKey and RowKey.

### Design Principles

1. **Denormalization**: Duplicate data for query performance
2. **PartitionKey Optimization**: Design for common query patterns
3. **No Joins**: All data in single table or denormalized
4. **Eventually Consistent**: Accept eventual consistency
5. **Query-First Design**: Schema optimized for read patterns

---

## 🗄️ Azure Table Storage

### Table: `Users`

Stores user profiles and authentication data.

**PartitionKey Strategy**: `USER#{firstLetter of userId}`  
**RowKey**: `userId` (GUID)

**Entity Schema**:
```csharp
public class UserEntity : ITableEntity
{
    // ITableEntity properties
    public string PartitionKey { get; set; }  // USER#A, USER#B, etc.
    public string RowKey { get; set; }        // user-guid
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // User properties
    public string Email { get; set; }
    public string PasswordHash { get; set; }
    public string Name { get; set; }
    public int Age { get; set; }
    public string Bio { get; set; }
    public string Location { get; set; }
    public string Gender { get; set; }  // male, female, non-binary, prefer-not-to-say
    public string ProfileImageUrl { get; set; }
    public bool IsOnline { get; set; }
    public DateTime LastSeen { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }  // Soft delete
}
```

**Indexes**: PartitionKey + RowKey (automatic)

**Example Entities**:
```json
{
  "PartitionKey": "USER#a",
  "RowKey": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "Email": "anna@example.com",
  "Name": "Анна",
  "Age": 25,
  "Location": "Москва"
}
```

**Common Queries**:
- Get user by ID: `PartitionKey = "USER#a" AND RowKey = "{userId}"`
- List users (paginated): Query with continuation token

---

### Table: `UserImages`

Stores references to user profile images.

**PartitionKey**: `userId`  
**RowKey**: `imageId` (GUID)

**Entity Schema**:
```csharp
public class UserImageEntity : ITableEntity
{
    public string PartitionKey { get; set; }  // userId
    public string RowKey { get; set; }        // imageId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string BlobUrl { get; set; }       // Full blob URL
    public int OrderIndex { get; set; }       // Display order (0-9)
    public DateTime UploadedAt { get; set; }
}
```

**Queries**:
- Get user's images: `PartitionKey = "{userId}"`

---

### Table: `UserPreferences`

User search and matching preferences.

**PartitionKey**: `userId`  
**RowKey**: `PREFS` (constant)

**Entity Schema**:
```csharp
public class UserPreferencesEntity : ITableEntity
{
    public string PartitionKey { get; set; }  // userId
    public string RowKey { get; set; }        // "PREFS"
    
    public int AgeRangeMin { get; set; }
    public int AgeRangeMax { get; set; }
    public int MaxDistanceKm { get; set; }
    public string ShowMe { get; set; }        // everyone, men, women, non-binary
    public DateTime UpdatedAt { get; set; }
}
```

---

### Table: `UserSettings`

User app settings and privacy.

**PartitionKey**: `userId`  
**RowKey**: `SETTINGS` (constant)

**Entity Schema**:
```csharp
public class UserSettingsEntity : ITableEntity
{
    public string PartitionKey { get; set; }  // userId
    public string RowKey { get; set; }        // "SETTINGS"
    
    public string ProfileVisibility { get; set; }  // public, private, friends
    public bool AnonymousLikes { get; set; }
    public string Language { get; set; }           // ru, en
    public bool NotificationsEnabled { get; set; }
    public bool EmailNotifications { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

---

### Table: `Likes`

User likes (sent to other users).

**PartitionKey**: `fromUserId`  
**RowKey**: `toUserId`

**Entity Schema**:
```csharp
public class LikeEntity : ITableEntity
{
    public string PartitionKey { get; set; }  // fromUserId
    public string RowKey { get; set; }        // toUserId
    
    public string FromUserId { get; set; }    // Redundant for ease
    public string ToUserId { get; set; }      // Redundant for ease
    public DateTime CreatedAt { get; set; }
    public bool IsMatch { get; set; }         // True if mutual like exists
}
```

**Queries**:
- Sent likes: `PartitionKey = "{userId}"`
- Received likes: Reverse query needed (see below)

**Reverse Index** (separate table or same table):
To efficiently query "who liked me", create reverse entries:
- When user A likes user B:
  - Store: `PartitionKey = A, RowKey = B` (sent like)
  - Store: `PartitionKey = B_RECEIVED, RowKey = A` (received like index)

---

### Table: `Matches`

Mutual likes (matches).

**PartitionKey**: `userId1` (lexicographically smaller)  
**RowKey**: `userId2` (lexicographically larger)

**Entity Schema**:
```csharp
public class MatchEntity : ITableEntity
{
    public string PartitionKey { get; set; }  // userId1
    public string RowKey { get; set; }        // userId2
    
    public string User1Id { get; set; }
    public string User2Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }        // False if unmatched
    public string ChatId { get; set; }        // Reference to chat
}
```

**Notes**:
- Always store userIds in consistent order to avoid duplicates
- Query matches for a user: `PartitionKey = "{userId}"`

---

### Table: `Events`

Events (concerts, meetups, etc.).

**PartitionKey**: `EVENT#{category}`  
**RowKey**: `eventId` (GUID)

**Entity Schema**:
```csharp
public class EventEntity : ITableEntity
{
    public string PartitionKey { get; set; }  // EVENT#concert, EVENT#meetup, etc.
    public string RowKey { get; set; }        // eventId
    
    public string Title { get; set; }
    public string Description { get; set; }
    public string ImageUrl { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Location { get; set; }
    public int? Capacity { get; set; }
    public string Category { get; set; }      // concert, meetup, festival, party, yachting
    public decimal? Price { get; set; }
    public string OrganizerId { get; set; }
    public bool IsSecret { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

**Queries**:
- All events: Query across all partitions (paginated)
- Events by category: `PartitionKey = "EVENT#{category}"`

---

### Table: `EventAttendees`

Event registrations.

**PartitionKey**: `eventId`  
**RowKey**: `userId`

**Entity Schema**:
```csharp
public class EventAttendeeEntity : ITableEntity
{
    public string PartitionKey { get; set; }  // eventId
    public string RowKey { get; set; }        // userId
    
    public DateTime RegisteredAt { get; set; }
    public bool Attended { get; set; }        // Post-event: did they show up?
}
```

**Queries**:
- Get event attendees: `PartitionKey = "{eventId}"`
- Get user's events: Need reverse index

**Reverse Index**: `UserEvents` table
- PartitionKey: `userId`
- RowKey: `eventId`

---

### Table: `Chats`

Chat metadata (private or group).

**PartitionKey**: `CHAT#{type}`  
**RowKey**: `chatId` (GUID)

**Entity Schema**:
```csharp
public class ChatEntity : ITableEntity
{
    public string PartitionKey { get; set; }  // CHAT#private or CHAT#group
    public string RowKey { get; set; }        // chatId
    
    public string Type { get; set; }          // private, group
    public string Name { get; set; }          // For group chats
    public string Description { get; set; }   // For group chats
    public string EventId { get; set; }       // For event chats
    public string Participants { get; set; }  // JSON array of userIds
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

---

### Table: `Messages`

Chat messages.

**PartitionKey**: `chatId`  
**RowKey**: `{timestamp-ticks-reversed}#{messageId}` (for chronological order)

**Entity Schema**:
```csharp
public class MessageEntity : ITableEntity
{
    public string PartitionKey { get; set; }  // chatId
    public string RowKey { get; set; }        // reversed timestamp + messageId
    
    public string MessageId { get; set; }
    public string SenderId { get; set; }
    public string Content { get; set; }
    public string Type { get; set; }          // text, image, system
    public DateTime Timestamp { get; set; }
    public bool IsRead { get; set; }
    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

**RowKey Design**:
- To query messages in chronological order (newest first):
- RowKey = `{DateTime.MaxValue.Ticks - timestamp.Ticks:D19}#{messageId}`
- This reverses the order for efficient "latest messages" queries

**Queries**:
- Latest messages: `PartitionKey = "{chatId}"` (auto-sorted by RowKey)
- Pagination: Use RowKey continuation token

---

### Table: `ForumSections`

Forum sections (categories).

**PartitionKey**: `FORUM`  
**RowKey**: `sectionId` (GUID or slug)

**Entity Schema**:
```csharp
public class ForumSectionEntity : ITableEntity
{
    public string PartitionKey { get; set; }  // "FORUM"
    public string RowKey { get; set; }        // sectionId
    
    public string Name { get; set; }
    public string Description { get; set; }
    public int OrderIndex { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

---

### Table: `ForumTopics`

Forum topics.

**PartitionKey**: `SECTION#{sectionId}`  
**RowKey**: `topicId` (GUID)

**Entity Schema**:
```csharp
public class ForumTopicEntity : ITableEntity
{
    public string PartitionKey { get; set; }  // SECTION#{sectionId}
    public string RowKey { get; set; }        // topicId
    
    public string Title { get; set; }
    public string Content { get; set; }
    public string AuthorId { get; set; }
    public bool IsPinned { get; set; }
    public bool IsLocked { get; set; }
    public int ReplyCount { get; set; }       // Denormalized
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

---

### Table: `ForumReplies`

Forum topic replies.

**PartitionKey**: `TOPIC#{topicId}`  
**RowKey**: `{timestamp-reversed}#{replyId}`

**Entity Schema**:
```csharp
public class ForumReplyEntity : ITableEntity
{
    public string PartitionKey { get; set; }  // TOPIC#{topicId}
    public string RowKey { get; set; }        // reversed timestamp + replyId
    
    public string ReplyId { get; set; }
    public string AuthorId { get; set; }
    public string Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

---

### Table: `StoreItems`

Store product catalog.

**PartitionKey**: `STORE#{category}`  
**RowKey**: `itemId` (GUID)

**Entity Schema**:
```csharp
public class StoreItemEntity : ITableEntity
{
    public string PartitionKey { get; set; }  // STORE#clothing, STORE#music, etc.
    public string RowKey { get; set; }        // itemId
    
    public string Title { get; set; }
    public string Description { get; set; }
    public decimal Price { get; set; }
    public string ImageUrl { get; set; }
    public string Category { get; set; }
    public int StockQuantity { get; set; }
    public bool IsAvailable { get; set; }
    public string ExternalPurchaseUrl { get; set; }  // Official band store
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

---

### Table: `BlogPosts`

Blog posts from the band.

**PartitionKey**: `BLOG`  
**RowKey**: `{timestamp-reversed}#{postId}` (newest first)

**Entity Schema**:
```csharp
public class BlogPostEntity : ITableEntity
{
    public string PartitionKey { get; set; }  // "BLOG"
    public string RowKey { get; set; }        // reversed timestamp + postId
    
    public string PostId { get; set; }
    public string Title { get; set; }
    public string Excerpt { get; set; }
    public string Content { get; set; }
    public string ImageUrl { get; set; }
    public string AuthorId { get; set; }
    public string Tags { get; set; }          // JSON array
    public DateTime PublishedAt { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

---

## 🗂️ Azure Blob Storage

### Container: `profile-images`

User profile photos.

**Blob Naming**: `{userId}/{imageId}.{ext}`

**Example**:
- `a1b2c3d4-e5f6-7890-abcd-ef1234567890/img-001.jpg`
- `a1b2c3d4-e5f6-7890-abcd-ef1234567890/img-002.jpg`

**Access**: Public read (for now), or SAS tokens

---

### Container: `event-images`

Event photos.

**Blob Naming**: `{eventId}/{imageId}.{ext}`

**Example**:
- `evt-12345678/banner.jpg`

---

### Container: `store-images`

Product images.

**Blob Naming**: `{itemId}/{imageId}.{ext}`

---

### Container: `blog-images`

Blog post images.

**Blob Naming**: `{postId}/{imageId}.{ext}`

---

## 🔍 Query Patterns & Performance

### Efficient Queries (Fast)

1. **Point Query** (PartitionKey + RowKey):
   ```csharp
   var user = await tableClient.GetEntityAsync<UserEntity>(
       partitionKey: "USER#a",
       rowKey: userId
   );
   ```
   **Performance**: ~5-10ms

2. **Partition Query** (All entities in a partition):
   ```csharp
   var attendees = tableClient.QueryAsync<EventAttendeeEntity>(
       filter: $"PartitionKey eq '{eventId}'"
   );
   ```
   **Performance**: ~10-50ms (depending on size)

### Inefficient Queries (Slow - Avoid)

1. **Table Scan** (No partition key):
   ```csharp
   var allUsers = tableClient.QueryAsync<UserEntity>(
       filter: $"Age gt 25"  // Scans entire table!
   );
   ```
   **Performance**: Seconds to minutes

**Solution**: Use secondary indexes or denormalization

---

## 🔄 Data Consistency Patterns

### Eventually Consistent

Azure Table Storage is eventually consistent. Plan for:

1. **Denormalized Counts**: Update async
2. **Match Creation**: Two writes (like + match)
3. **Reverse Indexes**: Write twice

### Example: Like → Match Flow

```
1. Write Like:
   PartitionKey: fromUserId, RowKey: toUserId

2. Check Reverse Like:
   PartitionKey: toUserId, RowKey: fromUserId

3. If exists, create Match:
   PartitionKey: userId1 (smaller), RowKey: userId2 (larger)

4. Update both Likes with IsMatch = true
```

---

## 📊 Estimated Storage Costs

### Azure Table Storage

**Pricing** (Standard, LRS):
- Storage: $0.045 per GB/month
- Transactions: $0.00036 per 10,000 transactions

**Example** (1000 users):
- Users: ~1 MB
- Likes: ~5 MB
- Messages: ~50 MB
- Events: ~1 MB
- **Total**: ~60 MB × $0.045 = **$0.003/month**

### Azure Blob Storage

**Pricing** (Hot tier, LRS):
- Storage: $0.018 per GB/month
- Write operations: $0.05 per 10,000

**Example** (1000 users, avg 3 photos):
- Profile images: 3000 × 500 KB = 1.5 GB
- Event images: 100 × 2 MB = 200 MB
- **Total**: ~1.7 GB × $0.018 = **$0.03/month**

**Combined Storage**: **~$0.03/month** (very cheap!)

---

## 🛠️ Development Tools

### Azure Storage Explorer

GUI tool for browsing/editing Azure Storage:
- Download: https://azure.microsoft.com/features/storage-explorer/
- Connect with connection string or account key
- View/edit table entities
- Upload/download blobs

### Azurite (Local Emulator)

Local Azure Storage emulator:
```bash
npm install -g azurite
azurite --location ./azurite-data
```

**Connection String**:
```
UseDevelopmentStorage=true
```

---

## 📚 References

- [Azure Table Storage Best Practices](https://docs.microsoft.com/azure/cosmos-db/table-storage-design)
- [Azure Storage Naming Conventions](https://docs.microsoft.com/rest/api/storageservices/naming-and-referencing-containers--blobs--and-metadata)
- [Query Performance](https://docs.microsoft.com/azure/cosmos-db/table-storage-design-guide)

---

**Next**: See [API.md](./API.md) for how these entities map to API responses
