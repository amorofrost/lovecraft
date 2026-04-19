using System.Security.Claims;
using System.Linq;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Helpers;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Admin;
using Lovecraft.Common.DTOs.Blog;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.DTOs.Store;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
[RequireStaffRole("admin")]
public class AdminController : ControllerBase
{
    private readonly IAppConfigService _appConfig;
    private readonly IEventInviteService _eventInvites;
    private readonly IEventService _events;
    private readonly IForumService _forum;
    private readonly IStoreService _store;
    private readonly IBlogService _blog;

    public AdminController(
        IAppConfigService appConfig,
        IEventInviteService eventInvites,
        IEventService events,
        IForumService forum,
        IStoreService store,
        IBlogService blog)
    {
        _appConfig = appConfig;
        _eventInvites = eventInvites;
        _events = events;
        _forum = forum;
        _store = store;
        _blog = blog;
    }

    [HttpGet("events")]
    public async Task<ActionResult<ApiResponse<List<EventDto>>>> GetEvents()
    {
        var list = await _events.GetEventsAdminAsync();
        return Ok(ApiResponse<List<EventDto>>.SuccessResponse(list));
    }

    [HttpGet("events/{eventId}")]
    public async Task<ActionResult<ApiResponse<EventDto>>> GetEvent(string eventId)
    {
        var e = await _events.GetEventByIdAdminAsync(eventId);
        if (e is null)
            return NotFound(ApiResponse<EventDto>.ErrorResponse("NOT_FOUND", "Event not found"));
        return Ok(ApiResponse<EventDto>.SuccessResponse(e));
    }

    [HttpPost("events")]
    public async Task<ActionResult<ApiResponse<EventDto>>> CreateEvent([FromBody] AdminEventWriteDto dto)
    {
        var e = await _events.CreateEventAsync(dto);
        return Ok(ApiResponse<EventDto>.SuccessResponse(e));
    }

    [HttpPut("events/{eventId}")]
    public async Task<ActionResult<ApiResponse<EventDto>>> UpdateEvent(string eventId, [FromBody] AdminEventWriteDto dto)
    {
        var e = await _events.UpdateEventAsync(eventId, dto);
        if (e is null)
            return NotFound(ApiResponse<EventDto>.ErrorResponse("NOT_FOUND", "Event not found"));
        return Ok(ApiResponse<EventDto>.SuccessResponse(e));
    }

    [HttpDelete("events/{eventId}")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteEvent(string eventId)
    {
        await _forum.DeleteTopicsForEventAsync(eventId);
        await _eventInvites.DeleteAllInvitesForEventAsync(eventId);
        var ok = await _events.DeleteEventAsync(eventId);
        if (!ok)
            return NotFound(ApiResponse<object?>.ErrorResponse("NOT_FOUND", "Event not found"));
        return Ok(ApiResponse<object?>.SuccessResponse(null));
    }

    [HttpPost("events/{eventId}/archive")]
    public async Task<ActionResult<ApiResponse<bool>>> SetArchive(string eventId, [FromBody] ArchiveEventRequestDto body)
    {
        var ok = await _events.SetEventArchivedAsync(eventId, body.Archived);
        if (!ok)
            return NotFound(ApiResponse<bool>.ErrorResponse("NOT_FOUND", "Event not found"));
        return Ok(ApiResponse<bool>.SuccessResponse(true));
    }

    [HttpGet("events/{eventId}/attendees")]
    public async Task<ActionResult<ApiResponse<List<EventAttendeeAdminDto>>>> GetAttendees(string eventId)
    {
        var e = await _events.GetEventByIdAdminAsync(eventId);
        if (e is null)
            return NotFound(ApiResponse<List<EventAttendeeAdminDto>>.ErrorResponse("NOT_FOUND", "Event not found"));
        var list = await _events.GetEventAttendeesAsync(eventId);
        return Ok(ApiResponse<List<EventAttendeeAdminDto>>.SuccessResponse(list));
    }

    [HttpDelete("events/{eventId}/attendees/{userId}")]
    public async Task<ActionResult<ApiResponse<bool>>> RemoveAttendee(string eventId, string userId)
    {
        var e = await _events.GetEventByIdAdminAsync(eventId);
        if (e is null)
            return NotFound(ApiResponse<bool>.ErrorResponse("NOT_FOUND", "Event not found"));
        var ok = await _events.RemoveEventAttendeeAsync(eventId, userId);
        if (!ok)
            return NotFound(ApiResponse<bool>.ErrorResponse("NOT_FOUND", "Attendee not found"));
        return Ok(ApiResponse<bool>.SuccessResponse(true));
    }

    [HttpGet("events/{eventId}/forum-topics")]
    public async Task<ActionResult<ApiResponse<List<ForumTopicDto>>>> GetEventForumTopics(string eventId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(ApiResponse<List<ForumTopicDto>>.ErrorResponse("UNAUTHORIZED", "Not authenticated"));

        var topics = await _forum.GetEventDiscussionTopicsAsync(userId, eventId, isElevated: true);
        if (topics is null)
            return NotFound(ApiResponse<List<ForumTopicDto>>.ErrorResponse("NOT_FOUND", "Event not found"));
        return Ok(ApiResponse<List<ForumTopicDto>>.SuccessResponse(topics));
    }

    [HttpPost("events/{eventId}/forum-topics")]
    public async Task<ActionResult<ApiResponse<ForumTopicDto>>> CreateEventForumTopic(
        string eventId,
        [FromBody] CreateTopicRequestDto body)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(ApiResponse<ForumTopicDto>.ErrorResponse("UNAUTHORIZED", "Not authenticated"));

        var name = User.FindFirstValue(ClaimTypes.Name) ?? userId;
        var topic = await _forum.CreateEventDiscussionTopicAsync(
            eventId,
            body.Title,
            body.Content,
            userId,
            name,
            body.NoviceVisible,
            body.NoviceCanReply,
            body.EventTopicVisibility,
            body.AllowedUserIds);
        return Ok(ApiResponse<ForumTopicDto>.SuccessResponse(topic));
    }

    [HttpPut("forum-topics/{topicId}")]
    public async Task<ActionResult<ApiResponse<ForumTopicDto>>> UpdateForumTopic(
        string topicId,
        [FromBody] UpdateTopicRequestDto body)
    {
        var t = await _forum.UpdateTopicAsync(topicId, body);
        if (t is null)
            return NotFound(ApiResponse<ForumTopicDto>.ErrorResponse("NOT_FOUND", "Topic not found"));
        return Ok(ApiResponse<ForumTopicDto>.SuccessResponse(t));
    }

    [HttpDelete("forum-topics/{topicId}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteForumTopic(string topicId)
    {
        var ok = await _forum.DeleteTopicAsync(topicId);
        if (!ok)
            return NotFound(ApiResponse<bool>.ErrorResponse("NOT_FOUND", "Topic not found"));
        return Ok(ApiResponse<bool>.SuccessResponse(true));
    }

    [HttpGet("forum-sections")]
    public async Task<ActionResult<ApiResponse<List<ForumSectionDto>>>> ListForumSections()
    {
        var list = await _forum.GetSectionsAsync();
        return Ok(ApiResponse<List<ForumSectionDto>>.SuccessResponse(list));
    }

    [HttpPost("forum-sections")]
    public async Task<ActionResult<ApiResponse<ForumSectionDto>>> CreateForumSection([FromBody] CreateForumSectionRequestDto body)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<ForumSectionDto>.ErrorResponse("VALIDATION_ERROR", "Validation failed"));
        if (body.Id.Equals("events", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<ForumSectionDto>.ErrorResponse("INVALID_ID", "Reserved section id"));
        try
        {
            var s = await _forum.CreateSectionAsync(body.Id, body.Name, body.Description, body.MinRank);
            return Ok(ApiResponse<ForumSectionDto>.SuccessResponse(s));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<ForumSectionDto>.ErrorResponse("INVALID_ID", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiResponse<ForumSectionDto>.ErrorResponse("DUPLICATE", ex.Message));
        }
    }

    [HttpPut("forum-sections/{sectionId}")]
    public async Task<ActionResult<ApiResponse<ForumSectionDto>>> UpdateForumSection(
        string sectionId,
        [FromBody] UpdateForumSectionRequestDto body)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<ForumSectionDto>.ErrorResponse("VALIDATION_ERROR", "Validation failed"));
        var s = await _forum.UpdateSectionAsync(sectionId, body.Name, body.Description, body.MinRank);
        if (s is null)
            return NotFound(ApiResponse<ForumSectionDto>.ErrorResponse("NOT_FOUND", "Section not found"));
        return Ok(ApiResponse<ForumSectionDto>.SuccessResponse(s));
    }

    [HttpDelete("forum-sections/{sectionId}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteForumSection(string sectionId)
    {
        var ok = await _forum.DeleteSectionAsync(sectionId);
        if (!ok)
            return NotFound(ApiResponse<bool>.ErrorResponse("NOT_FOUND", "Section not found or not allowed"));
        return Ok(ApiResponse<bool>.SuccessResponse(true));
    }

    [HttpPut("forum-sections/order")]
    public async Task<ActionResult<ApiResponse<bool>>> ReorderForumSections([FromBody] ReorderForumSectionsRequestDto body)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<bool>.ErrorResponse("VALIDATION_ERROR", "Validation failed"));
        var ok = await _forum.ReorderSectionsAsync(body.SectionIds);
        if (!ok)
            return BadRequest(ApiResponse<bool>.ErrorResponse("REORDER_FAILED", "Section list must match existing non-event sections."));
        return Ok(ApiResponse<bool>.SuccessResponse(true));
    }

    [HttpGet("forum-sections/{sectionId}/topics")]
    public async Task<ActionResult<ApiResponse<List<ForumTopicDto>>>> GetForumSectionTopics(string sectionId)
    {
        if (sectionId.Equals("events", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<List<ForumTopicDto>>.ErrorResponse("INVALID_SECTION", "Manage event threads in Events."));
        var section = (await _forum.GetSectionsAsync()).FirstOrDefault(s => s.Id == sectionId);
        if (section is null)
            return NotFound(ApiResponse<List<ForumTopicDto>>.ErrorResponse("NOT_FOUND", "Section not found"));
        var topics = await _forum.GetTopicsAsync(sectionId);
        return Ok(ApiResponse<List<ForumTopicDto>>.SuccessResponse(topics));
    }

    [HttpPost("forum-sections/{sectionId}/topics")]
    public async Task<ActionResult<ApiResponse<ForumTopicDto>>> CreateForumSectionTopic(
        string sectionId,
        [FromBody] CreateTopicRequestDto request)
    {
        if (sectionId.Equals("events", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<ForumTopicDto>.ErrorResponse("INVALID_SECTION", "Use Events admin for event threads."));
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<ForumTopicDto>.ErrorResponse("VALIDATION_ERROR", "Validation failed"));
        if (HtmlGuard.ContainsHtml(request.Title))
            return BadRequest(ApiResponse<ForumTopicDto>.ErrorResponse("HTML_NOT_ALLOWED", "HTML tags are not permitted in topic title"));
        if (HtmlGuard.ContainsHtml(request.Content))
            return BadRequest(ApiResponse<ForumTopicDto>.ErrorResponse("HTML_NOT_ALLOWED", "HTML tags are not permitted in topic content"));

        var authorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var authorName = User.FindFirstValue(ClaimTypes.Name) ?? authorId;
        if (string.IsNullOrEmpty(authorId))
            return Unauthorized(ApiResponse<ForumTopicDto>.ErrorResponse("UNAUTHORIZED", "Not authenticated"));

        try
        {
            var result = await _forum.CreateTopicAsync(
                sectionId, authorId, authorName!, request.Title, request.Content,
                request.NoviceVisible, request.NoviceCanReply);
            return Ok(ApiResponse<ForumTopicDto>.SuccessResponse(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ForumTopicDto>.ErrorResponse("NOT_FOUND", "Section not found"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<ForumTopicDto>.ErrorResponse("INTERNAL_ERROR", ex.Message));
        }
    }

    [HttpGet("store-items")]
    public async Task<ActionResult<ApiResponse<List<StoreItemDto>>>> ListStoreItems()
    {
        var items = await _store.GetStoreItemsAsync();
        return Ok(ApiResponse<List<StoreItemDto>>.SuccessResponse(items));
    }

    [HttpGet("store-items/{itemId}")]
    public async Task<ActionResult<ApiResponse<StoreItemDto>>> GetStoreItem(string itemId)
    {
        var item = await _store.GetStoreItemByIdAsync(itemId);
        if (item is null)
            return NotFound(ApiResponse<StoreItemDto>.ErrorResponse("NOT_FOUND", "Store item not found"));
        return Ok(ApiResponse<StoreItemDto>.SuccessResponse(item));
    }

    [HttpPost("store-items")]
    public async Task<ActionResult<ApiResponse<StoreItemDto>>> CreateStoreItem([FromBody] CreateStoreItemRequestDto body)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<StoreItemDto>.ErrorResponse("VALIDATION_ERROR", "Validation failed"));

        var dto = new StoreItemDto
        {
            Id = body.Id,
            Title = body.Title,
            Description = body.Description,
            Price = body.Price,
            ImageUrl = body.ImageUrl,
            Category = body.Category,
            ExternalPurchaseUrl = body.ExternalPurchaseUrl ?? string.Empty,
        };

        try
        {
            var created = await _store.CreateStoreItemAsync(dto);
            return Ok(ApiResponse<StoreItemDto>.SuccessResponse(created));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiResponse<StoreItemDto>.ErrorResponse("DUPLICATE", ex.Message));
        }
    }

    [HttpPut("store-items/{itemId}")]
    public async Task<ActionResult<ApiResponse<StoreItemDto>>> UpdateStoreItem(
        string itemId,
        [FromBody] StoreItemMutationDto body)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<StoreItemDto>.ErrorResponse("VALIDATION_ERROR", "Validation failed"));

        var dto = new StoreItemDto
        {
            Id = itemId,
            Title = body.Title,
            Description = body.Description,
            Price = body.Price,
            ImageUrl = body.ImageUrl,
            Category = body.Category,
            ExternalPurchaseUrl = body.ExternalPurchaseUrl ?? string.Empty,
        };

        var updated = await _store.UpdateStoreItemAsync(itemId, dto);
        if (updated is null)
            return NotFound(ApiResponse<StoreItemDto>.ErrorResponse("NOT_FOUND", "Store item not found"));
        return Ok(ApiResponse<StoreItemDto>.SuccessResponse(updated));
    }

    [HttpDelete("store-items/{itemId}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteStoreItem(string itemId)
    {
        var ok = await _store.DeleteStoreItemAsync(itemId);
        if (!ok)
            return NotFound(ApiResponse<bool>.ErrorResponse("NOT_FOUND", "Store item not found"));
        return Ok(ApiResponse<bool>.SuccessResponse(true));
    }

    [HttpGet("blog-posts")]
    public async Task<ActionResult<ApiResponse<List<BlogPostDto>>>> ListBlogPosts()
    {
        var posts = await _blog.GetBlogPostsAsync();
        return Ok(ApiResponse<List<BlogPostDto>>.SuccessResponse(posts));
    }

    [HttpGet("blog-posts/{postId}")]
    public async Task<ActionResult<ApiResponse<BlogPostDto>>> GetBlogPost(string postId)
    {
        var post = await _blog.GetBlogPostByIdAsync(postId);
        if (post is null)
            return NotFound(ApiResponse<BlogPostDto>.ErrorResponse("NOT_FOUND", "Blog post not found"));
        return Ok(ApiResponse<BlogPostDto>.SuccessResponse(post));
    }

    [HttpPost("blog-posts")]
    public async Task<ActionResult<ApiResponse<BlogPostDto>>> CreateBlogPost([FromBody] CreateBlogPostRequestDto body)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<BlogPostDto>.ErrorResponse("VALIDATION_ERROR", "Validation failed"));

        var dto = new BlogPostDto
        {
            Id = body.Id,
            Title = body.Title,
            Excerpt = body.Excerpt,
            Content = body.Content,
            ImageUrl = body.ImageUrl,
            Author = body.Author,
            Tags = body.Tags ?? new List<string>(),
            Date = body.Date,
        };

        try
        {
            var created = await _blog.CreateBlogPostAsync(dto);
            return Ok(ApiResponse<BlogPostDto>.SuccessResponse(created));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiResponse<BlogPostDto>.ErrorResponse("DUPLICATE", ex.Message));
        }
    }

    [HttpPut("blog-posts/{postId}")]
    public async Task<ActionResult<ApiResponse<BlogPostDto>>> UpdateBlogPost(
        string postId,
        [FromBody] BlogPostMutationDto body)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<BlogPostDto>.ErrorResponse("VALIDATION_ERROR", "Validation failed"));

        var dto = new BlogPostDto
        {
            Id = postId,
            Title = body.Title,
            Excerpt = body.Excerpt,
            Content = body.Content,
            ImageUrl = body.ImageUrl,
            Author = body.Author,
            Tags = body.Tags ?? new List<string>(),
            Date = body.Date,
        };

        var updated = await _blog.UpdateBlogPostAsync(postId, dto);
        if (updated is null)
            return NotFound(ApiResponse<BlogPostDto>.ErrorResponse("NOT_FOUND", "Blog post not found"));
        return Ok(ApiResponse<BlogPostDto>.SuccessResponse(updated));
    }

    [HttpDelete("blog-posts/{postId}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteBlogPost(string postId)
    {
        var ok = await _blog.DeleteBlogPostAsync(postId);
        if (!ok)
            return NotFound(ApiResponse<bool>.ErrorResponse("NOT_FOUND", "Blog post not found"));
        return Ok(ApiResponse<bool>.SuccessResponse(true));
    }

    /// <summary>
    /// Infrastructure snapshot for the frontend + backend containers.
    /// Uses the Docker Engine API via <c>/var/run/docker.sock</c>.
    /// </summary>
    [HttpGet("infrastructure")]
    public async Task<ActionResult<ApiResponse<InfrastructureStatusDto>>> GetInfrastructure()
    {
        var now = DateTime.UtcNow;
        var status = new InfrastructureStatusDto { GeneratedAtUtc = now };

        try
        {
            // These match docker-compose.yml container_name values.
            status.Containers.Add(await GetContainerInfraAsync("aloevera-backend", now));
            status.Containers.Add(await GetContainerInfraAsync("aloevera-frontend", now));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<InfrastructureStatusDto>.ErrorResponse("INFRA_ERROR", ex.Message));
        }

        return Ok(ApiResponse<InfrastructureStatusDto>.SuccessResponse(status));
    }

    [HttpGet("invites")]
    public async Task<ActionResult<ApiResponse<List<EventInviteAdminDto>>>> ListInvites()
    {
        var list = await _eventInvites.ListInvitesAsync();
        return Ok(ApiResponse<List<EventInviteAdminDto>>.SuccessResponse(list.ToList()));
    }

    [HttpGet("events/{eventId}/invites")]
    public async Task<ActionResult<ApiResponse<List<EventInviteAdminDto>>>> ListInvitesForEvent(string eventId)
    {
        var all = await _eventInvites.ListInvitesAsync();
        var filtered = all.Where(i => string.Equals(i.EventId, eventId, StringComparison.Ordinal)).ToList();
        return Ok(ApiResponse<List<EventInviteAdminDto>>.SuccessResponse(filtered));
    }

    [HttpPost("invites/campaigns")]
    public async Task<ActionResult<ApiResponse<CreateEventInviteResponseDto>>> CreateCampaignInvite(
        [FromBody] CreateCampaignInviteRequestDto request)
    {
        var (plain, exp) = await _eventInvites.CreateCampaignInviteAsync(
            request.CampaignId,
            request.CampaignLabel,
            request.ExpiresAtUtc,
            request.PlainCode);
        return Ok(ApiResponse<CreateEventInviteResponseDto>.SuccessResponse(
            new CreateEventInviteResponseDto(plain, exp)));
    }

    /// <summary>Issue or rotate the invite for an event (plaintext stored in table; returned here).</summary>
    [HttpPost("events/{eventId}/invites")]
    public async Task<ActionResult<ApiResponse<CreateEventInviteResponseDto>>> CreateEventInvite(
        string eventId,
        [FromBody] CreateEventInviteRequestDto request)
    {
        var (plain, exp) = await _eventInvites.CreateOrRotateInviteAsync(eventId, request.ExpiresAtUtc, request.PlainCode);
        return Ok(ApiResponse<CreateEventInviteResponseDto>.SuccessResponse(
            new CreateEventInviteResponseDto(plain, exp)));
    }

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig()
    {
        var cfg = await _appConfig.GetConfigAsync();
        var dto = new AppConfigDto(
            RankThresholds: new()
            {
                ["active_replies"] = cfg.Ranks.ActiveReplies.ToString(),
                ["active_likes"] = cfg.Ranks.ActiveLikes.ToString(),
                ["active_events"] = cfg.Ranks.ActiveEvents.ToString(),
                ["friend_replies"] = cfg.Ranks.FriendReplies.ToString(),
                ["friend_likes"] = cfg.Ranks.FriendLikes.ToString(),
                ["friend_events"] = cfg.Ranks.FriendEvents.ToString(),
                ["crew_replies"] = cfg.Ranks.CrewReplies.ToString(),
                ["crew_likes"] = cfg.Ranks.CrewLikes.ToString(),
                ["crew_events"] = cfg.Ranks.CrewEvents.ToString(),
                ["crew_matches"] = cfg.Ranks.CrewMatches.ToString(),
            },
            Permissions: new()
            {
                ["create_topic"] = cfg.Permissions.CreateTopic,
                ["delete_own_reply"] = cfg.Permissions.DeleteOwnReply,
                ["delete_any_reply"] = cfg.Permissions.DeleteAnyReply,
                ["delete_any_topic"] = cfg.Permissions.DeleteAnyTopic,
                ["pin_topic"] = cfg.Permissions.PinTopic,
                ["ban_user"] = cfg.Permissions.BanUser,
                ["assign_role"] = cfg.Permissions.AssignRole,
                ["override_rank"] = cfg.Permissions.OverrideRank,
                ["manage_events"] = cfg.Permissions.ManageEvents,
                ["manage_blog"] = cfg.Permissions.ManageBlog,
                ["manage_store"] = cfg.Permissions.ManageStore,
            },
            Registration: new()
            {
                ["require_event_invite"] = cfg.Registration.RequireEventInvite ? "true" : "false",
            });
        return Ok(ApiResponse<AppConfigDto>.SuccessResponse(dto));
    }

    private static async Task<ContainerInfrastructureDto> GetContainerInfraAsync(string containerName, DateTime nowUtc)
    {
        var socketPath = "/var/run/docker.sock";
        if (!System.IO.File.Exists(socketPath))
            throw new InvalidOperationException("Docker socket not available. Mount /var/run/docker.sock into the backend container (read-only).");

        using var http = CreateDockerHttpClient(socketPath);

        var inspect = await http.GetFromJsonAsync<DockerInspectResponse>(
            $"/containers/{Uri.EscapeDataString(containerName)}/json") ?? throw new InvalidOperationException($"Docker inspect returned null for '{containerName}'.");

        var startedAt = inspect.State?.StartedAt ?? throw new InvalidOperationException($"Docker inspect missing StartedAt for '{containerName}'.");
        var uptime = Math.Max(0, (nowUtc - startedAt).TotalSeconds);

        var stats = await http.GetFromJsonAsync<DockerStatsResponse>(
            $"/containers/{Uri.EscapeDataString(containerName)}/stats?stream=false") ?? throw new InvalidOperationException($"Docker stats returned null for '{containerName}'.");

        var cpuPct = ComputeDockerCpuPercent(stats);
        var (memUsage, memLimit) = ComputeDockerMemory(stats);

        return new ContainerInfrastructureDto
        {
            Name = containerName,
            StartedAtUtc = startedAt,
            UptimeSeconds = uptime,
            CpuPercent = cpuPct,
            MemoryUsageBytes = memUsage,
            MemoryLimitBytes = memLimit,
        };
    }

    private static HttpClient CreateDockerHttpClient(string socketPath)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            },
        };
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost"),
            Timeout = TimeSpan.FromSeconds(2),
        };
        return http;
    }

    private static double ComputeDockerCpuPercent(DockerStatsResponse stats)
    {
        var cpuDelta = (stats.CpuStats?.CpuUsage?.TotalUsage ?? 0) - (stats.PreCpuStats?.CpuUsage?.TotalUsage ?? 0);
        var systemDelta = (stats.CpuStats?.SystemCpuUsage ?? 0) - (stats.PreCpuStats?.SystemCpuUsage ?? 0);
        if (cpuDelta <= 0 || systemDelta <= 0)
            return 0;

        var online = stats.CpuStats?.OnlineCpus;
        if (online is null || online <= 0)
        {
            // Fallback for older engines.
            online = stats.CpuStats?.CpuUsage?.PercpuUsage?.Count ?? 1;
        }

        return (cpuDelta / (double)systemDelta) * online.Value * 100.0;
    }

    private static (long usage, long limit) ComputeDockerMemory(DockerStatsResponse stats)
    {
        var usage = stats.MemoryStats?.Usage ?? 0;
        var limit = stats.MemoryStats?.Limit ?? 0;

        // On Linux, Docker's "usage" includes cache; subtract if present.
        var cache = stats.MemoryStats?.Stats != null && stats.MemoryStats.Stats.TryGetValue("cache", out var c) ? c : 0;
        var effective = usage - cache;
        if (effective < 0) effective = usage;

        return (effective, limit);
    }

    // --- Minimal Docker API response models (only what we need) ----------------

    private sealed class DockerInspectResponse
    {
        public DockerInspectState? State { get; set; }
    }

    private sealed class DockerInspectState
    {
        public DateTime StartedAt { get; set; }
    }

    private sealed class DockerStatsResponse
    {
        [JsonPropertyName("cpu_stats")]
        public DockerCpuStats? CpuStats { get; set; }

        [JsonPropertyName("precpu_stats")]
        public DockerCpuStats? PreCpuStats { get; set; }

        [JsonPropertyName("memory_stats")]
        public DockerMemoryStats? MemoryStats { get; set; }
    }

    private sealed class DockerCpuStats
    {
        [JsonPropertyName("cpu_usage")]
        public DockerCpuUsage? CpuUsage { get; set; }

        [JsonPropertyName("system_cpu_usage")]
        public long SystemCpuUsage { get; set; }

        [JsonPropertyName("online_cpus")]
        public int? OnlineCpus { get; set; }
    }

    private sealed class DockerCpuUsage
    {
        [JsonPropertyName("total_usage")]
        public long TotalUsage { get; set; }

        [JsonPropertyName("percpu_usage")]
        public List<long>? PercpuUsage { get; set; }
    }

    private sealed class DockerMemoryStats
    {
        [JsonPropertyName("usage")]
        public long Usage { get; set; }

        [JsonPropertyName("limit")]
        public long Limit { get; set; }

        [JsonPropertyName("stats")]
        public Dictionary<string, long>? Stats { get; set; }
    }
}
