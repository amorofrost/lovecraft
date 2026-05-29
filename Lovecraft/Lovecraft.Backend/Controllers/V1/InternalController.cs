using Lovecraft.Backend.Attributes;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Metrics;
using Lovecraft.Common.DTOs.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/internal")]
[RequireServiceToken]
public class InternalController : ControllerBase
{
    private readonly IUserService _users;
    private readonly INotificationPreferenceService _prefs;
    private readonly IMetricsCollector _metrics;

    public InternalController(IUserService users, INotificationPreferenceService prefs, IMetricsCollector metrics)
    {
        _users = users;
        _prefs = prefs;
        _metrics = metrics;
    }

    [HttpPost("notifications/mute-type")]
    public async Task<IActionResult> MuteType([FromBody] InternalMuteTypeRequestDto request)
    {
        if (string.IsNullOrEmpty(request.TelegramUserId) || string.IsNullOrEmpty(request.Type))
            return BadRequest();

        var userId = await _users.GetUserIdByTelegramIdAsync(request.TelegramUserId);
        if (userId is null)
            return NotFound();

        await _prefs.SetChannelDisabledForTypeAsync(userId, request.Type, "telegram");
        return NoContent();
    }

    [HttpPost("metrics/container-stats")]
    public IActionResult ContainerStats([FromBody] ContainerStatsIngestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Container)) return BadRequest();

        if (request.GcHeapMb is not null)
            _metrics.RecordTiming("container_stats", $"{request.Container}|gc_heap_mb", request.GcHeapMb.Value);
        if (request.WorkingSetMb is not null)
            _metrics.RecordTiming("container_stats", $"{request.Container}|working_set_mb", request.WorkingSetMb.Value);
        if (request.ThreadCount is not null)
            _metrics.RecordTiming("container_stats", $"{request.Container}|thread_count", request.ThreadCount.Value);
        if (request.CpuPercent is not null)
            _metrics.RecordTiming("container_stats", $"{request.Container}|cpu_percent", request.CpuPercent.Value);

        return NoContent();
    }
}

public sealed class ContainerStatsIngestDto
{
    public string Container { get; set; } = string.Empty;
    public long? GcHeapMb { get; set; }
    public long? WorkingSetMb { get; set; }
    public int? ThreadCount { get; set; }
    public double? CpuPercent { get; set; }
}
