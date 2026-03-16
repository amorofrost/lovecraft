using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Matching;

namespace Lovecraft.Backend.Services.Azure;

public class AzureMatchingService : IMatchingService
{
    private readonly TableClient _likesTable;
    private readonly TableClient _likesReceivedTable;
    private readonly TableClient _matchesTable;
    private readonly IChatService _chatService;
    private readonly ILogger<AzureMatchingService> _logger;

    public AzureMatchingService(TableServiceClient tableServiceClient, IChatService chatService, ILogger<AzureMatchingService> logger)
    {
        _chatService = chatService;
        _logger = logger;
        _likesTable = tableServiceClient.GetTableClient(TableNames.Likes);
        _likesReceivedTable = tableServiceClient.GetTableClient(TableNames.LikesReceived);
        _matchesTable = tableServiceClient.GetTableClient(TableNames.Matches);

        Task.WhenAll(
            _likesTable.CreateIfNotExistsAsync(),
            _likesReceivedTable.CreateIfNotExistsAsync(),
            _matchesTable.CreateIfNotExistsAsync()
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
                Like = ToLikeDto(existingLike),
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

        MatchDto? matchDto = null;
        if (isMutual)
        {
            // Update the reverse like to also mark as match
            try
            {
                var reverseLikeResponse = await _likesTable.GetEntityAsync<LikeEntity>(toUserId, fromUserId);
                var reverseLike = reverseLikeResponse.Value;
                reverseLike.IsMatch = true;
                await _likesTable.UpdateEntityAsync(reverseLike, reverseLike.ETag);
            }
            catch (RequestFailedException) { }

            try
            {
                var reverseReceivedResponse = await _likesReceivedTable.GetEntityAsync<LikeEntity>(fromUserId, toUserId);
                var reverseReceived = reverseReceivedResponse.Value;
                reverseReceived.IsMatch = true;
                await _likesReceivedTable.UpdateEntityAsync(reverseReceived, reverseReceived.ETag);
            }
            catch (RequestFailedException) { }

            var matchId = Guid.NewGuid().ToString();
            var chatId = Guid.NewGuid().ToString();

            var matchForFrom = new MatchEntity
            {
                PartitionKey = fromUserId,
                RowKey = matchId,
                MatchId = matchId,
                OtherUserId = toUserId,
                CreatedAt = now,
                ChatId = chatId
            };
            var matchForTo = new MatchEntity
            {
                PartitionKey = toUserId,
                RowKey = matchId,
                MatchId = matchId,
                OtherUserId = fromUserId,
                CreatedAt = now,
                ChatId = chatId
            };

            await Task.WhenAll(
                _matchesTable.UpsertEntityAsync(matchForFrom),
                _matchesTable.UpsertEntityAsync(matchForTo)
            );

            matchDto = new MatchDto
            {
                Id = matchId,
                Users = new List<string> { fromUserId, toUserId },
                CreatedAt = now
            };

            await _chatService.GetOrCreateChatAsync(fromUserId, toUserId);
            _logger.LogInformation("Match created between {From} and {To}", fromUserId, toUserId);
        }

        return new LikeResponseDto
        {
            Like = ToLikeDto(likeEntity),
            IsMatch = isMutual,
            Match = matchDto
        };
    }

    public async Task<List<LikeDto>> GetSentLikesAsync(string userId)
    {
        var results = new List<LikeDto>();
        await foreach (var entity in _likesTable.QueryAsync<LikeEntity>(
            filter: $"PartitionKey eq '{userId}'"))
        {
            results.Add(ToLikeDto(entity));
        }
        return results;
    }

    public async Task<List<LikeDto>> GetReceivedLikesAsync(string userId)
    {
        var results = new List<LikeDto>();
        await foreach (var entity in _likesReceivedTable.QueryAsync<LikeEntity>(
            filter: $"PartitionKey eq '{userId}'"))
        {
            results.Add(ToLikeDto(entity));
        }
        return results;
    }

    public async Task<List<MatchDto>> GetMatchesAsync(string userId)
    {
        var results = new List<MatchDto>();
        await foreach (var entity in _matchesTable.QueryAsync<MatchEntity>(
            filter: $"PartitionKey eq '{userId}'"))
        {
            results.Add(new MatchDto
            {
                Id = entity.MatchId,
                Users = new List<string> { userId, entity.OtherUserId },
                CreatedAt = entity.CreatedAt
            });
        }
        return results;
    }

    private static LikeDto ToLikeDto(LikeEntity entity) => new LikeDto
    {
        Id = entity.LikeId,
        FromUserId = entity.FromUserId,
        ToUserId = entity.ToUserId,
        CreatedAt = entity.CreatedAt,
        IsMatch = entity.IsMatch
    };
}
