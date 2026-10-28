using System.ComponentModel.DataAnnotations;

namespace FoodJS.Api.Features.Menu;

public sealed record MenuItemResponse(
    string Id,
    string Name,
    string Description,
    string CategoryId,
    string Category,
    decimal Price,
    int PrepTime,
    bool IsAvailable,
    int SoldCount,
    string? Image,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class SaveMenuItemRequest
{
    [Required, StringLength(120)]
    public required string Name { get; init; }

    [Required, StringLength(50)]
    public required string CategoryId { get; init; }

    [Range(0.01, 1_000_000)]
    public decimal Price { get; init; }

    [Range(1, 1_440)]
    public int PrepTime { get; init; }

    public bool IsAvailable { get; init; } = true;

    public string? Image { get; init; }

    [StringLength(2_000)]
    public string Description { get; init; } = string.Empty;
}
