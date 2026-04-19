using System.ComponentModel.DataAnnotations;

namespace Lovecraft.Common.DTOs.Store;

public class StoreItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    /// <summary>Direct link to this product on the official band merchandise site.</summary>
    public string ExternalPurchaseUrl { get; set; } = string.Empty;
}

/// <summary>Shared fields for create/update store items (admin).</summary>
public class StoreItemMutationDto
{
    [Required]
    [StringLength(300, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [StringLength(20000)]
    public string Description { get; set; } = string.Empty;

    [Range(0, 100_000_000)]
    public decimal Price { get; set; }

    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string ImageUrl { get; set; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Category { get; set; } = string.Empty;

    /// <summary>Product page on the official store; empty if not set.</summary>
    [StringLength(2000)]
    public string ExternalPurchaseUrl { get; set; } = string.Empty;
}

public class CreateStoreItemRequestDto : StoreItemMutationDto
{
    [Required]
    [RegularExpression(@"^[a-z0-9][a-z0-9_-]{0,62}$", ErrorMessage = "Id must be a lowercase slug (a-z, digits, hyphen, underscore).")]
    public string Id { get; set; } = string.Empty;
}
