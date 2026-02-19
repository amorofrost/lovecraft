using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Backend.MockData;

namespace Lovecraft.Backend.Services;

public class MockForumService : IForumService
{
    public Task<List<ForumSectionDto>> GetSectionsAsync()
    {
        return Task.FromResult(MockDataStore.ForumSections);
    }

    public Task<List<ForumTopicDto>> GetTopicsAsync(string sectionId)
    {
        var topics = MockDataStore.ForumTopics
            .Where(t => t.SectionId == sectionId)
            .ToList();
        return Task.FromResult(topics);
    }

    public Task<ForumTopicDto?> GetTopicByIdAsync(string topicId)
    {
        var topic = MockDataStore.ForumTopics.FirstOrDefault(t => t.Id == topicId);
        return Task.FromResult(topic);
    }

    public Task<List<ForumReplyDto>> GetRepliesAsync(string topicId)
    {
        var replies = MockDataStore.ForumReplies
            .Where(r => r.TopicId == topicId)
            .OrderBy(r => r.CreatedAt)
            .ToList();
        return Task.FromResult(replies);
    }

    public Task<ForumReplyDto> CreateReplyAsync(string topicId, string authorId, string authorName, string content)
    {
        var reply = new ForumReplyDto
        {
            Id = $"r_{Guid.NewGuid():N}",
            TopicId = topicId,
            AuthorId = authorId,
            AuthorName = authorName,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            Likes = 0,
        };
        MockDataStore.ForumReplies.Add(reply);

        var topic = MockDataStore.ForumTopics.FirstOrDefault(t => t.Id == topicId);
        if (topic != null)
        {
            topic.ReplyCount++;
            topic.UpdatedAt = reply.CreatedAt;
        }

        return Task.FromResult(reply);
    }
}
