using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lovecraft.Common.DTOs.Store;
using Lovecraft.Common.Models;
using Lovecraft.Backend.Services;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize] // Require authentication
public class StoreController : ControllerBase
{
    private readonly IStoreService _storeService;
    private readonly ILogger<StoreController> _logger;

    public StoreController(IStoreService storeService, ILogger<StoreController> logger)
    {
        _storeService = storeService;
        _logger = logger;
    }

    /// <summary>
    /// Get all store items
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<StoreItemDto>>>> GetStoreItems()
    {
        try
        {
            var items = await _storeService.GetStoreItemsAsync();
            return Ok(ApiResponse<List<StoreItemDto>>.SuccessResponse(items));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting store items");
            return StatusCode(500, ApiResponse<List<StoreItemDto>>.ErrorResponse("INTERNAL_ERROR", "Failed to get store items"));
        }
    }

    /// <summary>
    /// Get store item by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<StoreItemDto>>> GetStoreItem(string id)
    {
        try
        {
            var item = await _storeService.GetStoreItemByIdAsync(id);
            if (item == null)
            {
                return NotFound(ApiResponse<StoreItemDto>.ErrorResponse("NOT_FOUND", "Store item not found"));
            }
            return Ok(ApiResponse<StoreItemDto>.SuccessResponse(item));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting store item {ItemId}", id);
            return StatusCode(500, ApiResponse<StoreItemDto>.ErrorResponse("INTERNAL_ERROR", "Failed to get store item"));
        }
    }
}
