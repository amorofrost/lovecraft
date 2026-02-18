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
        // Return empty list for now - topics can be added later
        return Task.FromResult(new List<ForumTopicDto>());
    }
}
