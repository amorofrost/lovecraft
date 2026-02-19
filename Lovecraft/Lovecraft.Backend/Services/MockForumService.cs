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
}
