using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Features;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lovecraft.Backend.Controllers.V1;

/// <summary>
/// Public feature flags endpoint. Reads from <c>appconfig.features</c> partition.
/// Anonymous-accessible by design — the frontend calls it during app bootstrap
/// (before login) to determine which routes / nav buttons to render.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[AllowAnonymous]
public class FeaturesController : ControllerBase
{
    private readonly IAppConfigService _appConfig;

    public FeaturesController(IAppConfigService appConfig)
    {
        _appConfig = appConfig;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<FeatureFlagsDto>>> GetFlags()
    {
        var cfg = await _appConfig.GetConfigAsync();
        return Ok(ApiResponse<FeatureFlagsDto>.SuccessResponse(new FeatureFlagsDto
        {
            FeedEnabled = cfg.Features.FeedEnabled,
        }));
    }
}
