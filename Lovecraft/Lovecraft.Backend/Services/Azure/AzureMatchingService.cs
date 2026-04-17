using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Matching;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Services.Azure;

public class AzureMatchingService : IMatchingService
{
    private readonly TableClient _likesTable;
    private readonly TableClient _likesReceivedTable;
    private readonly IChatService _chatService;
    private readonly IUserService _userService;
    private readonly ILogger<AzureMatchingService> _logger;

    public AzureMatchingService(
        TableServiceClient tableServiceClient,
        IChatService chatService,
        IUserService userService,
        ILogger<AzureMatchingService> logger)
    {
        _chatService = chatService;
        _userService = userService;
        _logger = logger;
        _likesTable = tableServiceClient.GetTableClient(TableNames.Likes);
        _likesReceivedTable = tableServiceClient.GetTableClient(TableNames.LikesReceived);

        Task.WhenAll(
            _likesTable.CreateIfNotExistsAsync(),
            _likesReceivedTable.CreateIfNotExistsAsync()
        ).GetAwaiter().GetResult();
    }

    public async Task<LikeResponseDto> CreateLikeAsync(string fromUserId, string toUserId)
    {
        // Check if like already sent
        bool alreadyLiked = false;
        try
        {
            await _likesTable.GetEntityAsync<LikeEntity>(fromUserId, toUserId);
            alreadyLiked = true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        if (alreadyLiked)
        {
            // Return existing like without creating a match
            var existingLike = (await _likesTable.GetEntityAsync<LikeEntity>(fromUserId, toUserId)).Value;
            return new LikeResponseDto
            {
                Like = ToSentLikeDto(existingLike),
                IsMatch = existingLike.IsMatch
            };
        }

        // Check if there's a reverse like (mutual match)
        bool isMutual = false;
        try
        {
            await _likesTable.GetEntityAsync<LikeEntity>(toUserId, fromUserId);
            isMutual = true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        var now = DateTime.UtcNow;
        var likeId = Guid.NewGuid().ToString();

        var likeEntity = new LikeEntity
        {
            PartitionKey = fromUserId,
            RowKey = toUserId,
            LikeId = likeId,
            FromUserId = fromUserId,
            ToUserId = toUserId,
            CreatedAt = now,
            IsMatch = isMutual
        };

        var likeReceivedEntity = new LikeEntity
        {
            PartitionKey = toUserId,
            RowKey = fromUserId,
            LikeId = likeId,
            FromUserId = fromUserId,
            ToUserId = toUserId,
            CreatedAt = now,
            IsMatch = isMutual
        };

        await Task.WhenAll(
            _likesTable.UpsertEntityAsync(likeEntity),
            _likesReceivedTable.UpsertEntityAsync(likeReceivedEntity)
        );

        await _userService.IncrementCounterAsync(toUserId, UserCounter.LikesReceived);

        MatchDto? matchDto = null;
        if (isMutual)
        {
            var sorted = new[] { fromUserId, toUserId }.OrderBy(x => x).ToArray();
            matchDto = new MatchDto
            {
                Id = $"{sorted[0]}_{sorted[1]}",
                Users = new List<string> { fromUserId, toUserId },
                CreatedAt = now
            };

            await _chatService.GetOrCreateChatAsync(fromUserId, toUserId);
            await _userService.IncrementCounterAsync(fromUserId, UserCounter.MatchCount);
            await _userService.IncrementCounterAsync(toUserId, UserCounter.MatchCount);
            _logger.LogInformation("Match created between {From} and {To}", fromUserId, toUserId);
        }

        return new LikeResponseDto
        {
            Like = ToSentLikeDto(likeEntity),
            IsMatch = isMutual,
            Match = matchDto
        };
    }

    public async Task<List<LikeDto>> GetSentLikesAsync(string userId)
    {
        var results = new List<LikeDto>();
        // likes table: PK = fromUserId, RK = toUserId
        await foreach (var entity in _likesTable.QueryAsync<LikeEntity>(
            filter: $"PartitionKey eq '{userId}'"))
        {
            results.Add(ToSentLikeDto(entity));
        }
        return results;
    }

    public async Task<List<LikeDto>> GetReceivedLikesAsync(string userId)
    {
        var results = new List<LikeDto>();
        // likesreceived table: PK = toUserId (recipient), RK = fromUserId (sender)
        await foreach (var entity in _likesReceivedTable.QueryAsync<LikeEntity>(
            filter: $"PartitionKey eq '{userId}'"))
        {
            results.Add(ToReceivedLikeDto(entity));
        }
        return results;
    }

    public async Task<List<MatchDto>> GetMatchesAsync(string userId)
    {
        // A match = userId liked otherUser AND otherUser liked userId
        // Compute from intersection: liked (likes PK=userId) ∩ likedBy (likesreceived PK=userId)
        var likedUserIds = new HashSet<string>();
        await foreach (var e in _likesTable.QueryAsync<LikeEntity>(filter: $"PartitionKey eq '{userId}'"))
            likedUserIds.Add(e.RowKey);

        var matches = new List<MatchDto>();
        await foreach (var e in _likesReceivedTable.QueryAsync<LikeEntity>(filter: $"PartitionKey eq '{userId}'"))
        {
            var otherUserId = e.RowKey; // RK = sender in likesreceived
            if (!likedUserIds.Contains(otherUserId)) continue;

            var sorted = new[] { userId, otherUserId }.OrderBy(x => x).ToArray();
            matches.Add(new MatchDto
            {
                Id = $"{sorted[0]}_{sorted[1]}",
                Users = new List<string> { userId, otherUserId },
                CreatedAt = e.CreatedAt > DateTime.MinValue ? e.CreatedAt : DateTime.UtcNow
            });
        }
        return matches;
    }

    // likes table: PK = fromUserId, RK = toUserId — derive authoritatively from PK/RK
    private static LikeDto ToSentLikeDto(LikeEntity entity) => new LikeDto
    {
        Id = string.IsNullOrEmpty(entity.LikeId) ? $"{entity.PartitionKey}_{entity.RowKey}" : entity.LikeId,
        FromUserId = entity.PartitionKey,
        ToUserId = entity.RowKey,
        CreatedAt = entity.CreatedAt > DateTime.MinValue ? entity.CreatedAt : DateTime.UtcNow,
        IsMatch = entity.IsMatch
    };

    // likesreceived table: PK = toUserId (recipient), RK = fromUserId (sender)
    private static LikeDto ToReceivedLikeDto(LikeEntity entity) => new LikeDto
    {
        Id = string.IsNullOrEmpty(entity.LikeId) ? $"{entity.RowKey}_{entity.PartitionKey}" : entity.LikeId,
        FromUserId = entity.RowKey,
        ToUserId = entity.PartitionKey,
        CreatedAt = entity.CreatedAt > DateTime.MinValue ? entity.CreatedAt : DateTime.UtcNow,
        IsMatch = entity.IsMatch
    };
}
