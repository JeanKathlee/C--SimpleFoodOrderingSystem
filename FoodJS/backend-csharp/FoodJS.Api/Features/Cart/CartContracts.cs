namespace FoodJS.Api.Features.Cart;

public sealed record CartItemResponse(
    int Id,
    int UserId,
    string MenuItemId,
    string Name,
    decimal Price,
    int Quantity,
    string? SelectedChoice,
    string? Image
);

public sealed record AddCartItemRequest(
    string? MenuItemId,
    int? Quantity,
    string? SelectedChoice
);

public sealed record UpdateCartItemRequest(
    int? Quantity
);
