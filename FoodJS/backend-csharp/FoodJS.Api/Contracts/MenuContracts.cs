using System.ComponentModel.DataAnnotations;

namespace FoodJS.Api.Contracts;

public sealed class MenuPageRequest
{
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;
    [Range(1, 50)]
    public int PageSize { get; init; } = 10;
    [StringLength(120)]
    public string? Search { get; init; }
    public string? CategoryId { get; init; }
    public bool? IsAvailable { get; init; }
}

public sealed record MenuPageResponse(
    IReadOnlyList<MenuItemResponse> Items, int TotalCount, int Page, int PageSize, int TotalPages);

// The shape of a menu item in the API response.
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
    DateTimeOffset UpdatedAt,
    IReadOnlyList<DrinkOptionResponse> DrinkOptions);

public sealed record DrinkOptionResponse(string Label, decimal AddOn);
public sealed record SaveDrinkOptionRequest(string? Label, decimal? AddOn);

// The shape of the request body for creating or updating a menu item. All fields are optional to allow partial updates.
public sealed class SaveMenuItemRequest
{
    public string? Name { get; init; }

    public string? CategoryId { get; init; }

    public decimal? Price { get; init; }

    public int? PrepTime { get; init; }

    // If null, the current availability is kept on update; if false, the item is marked unavailable; if true, the item is marked available.
    public bool? IsAvailable { get; init; }

    // If null, the current image is kept on update; if empty string, the image is removed; if non-empty string, the image is updated.
    public string? Image { get; init; }

    public string? Description { get; init; }

    // Omit to preserve options on update; omitted on create defaults to standalone.
    // [] explicitly disables drink choices for this item.
    // Exactly one option must cost 0 (included) when choices are configured.
    public List<SaveDrinkOptionRequest?>? DrinkOptions { get; init; }
}

public sealed class SetAvailabilityRequest
{
    public bool? IsAvailable { get; init; }
}
