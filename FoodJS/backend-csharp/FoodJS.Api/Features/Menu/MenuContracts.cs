namespace FoodJS.Api.Features.Menu;

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
    DateTimeOffset UpdatedAt);

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
}

public sealed class SetAvailabilityRequest
{
    public bool? IsAvailable { get; init; }
}
