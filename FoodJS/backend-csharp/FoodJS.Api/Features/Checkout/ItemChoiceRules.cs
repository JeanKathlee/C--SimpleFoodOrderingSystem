using FoodJS.Api.Common;

namespace FoodJS.Api.Features.Checkout;

public static class ItemChoiceRules
{
    // Keep these labels/prices aligned with ProductDetails.jsx. Never accept an add-on price from the client.
    public static decimal GetAddOnPrice(string? choice) => choice switch
    {
        null => 0m, // Existing carts/orders can have no choice.
        "Go Regular Iced Tea" => 0m,
        "Go Medium Iced Tea" => 45m,
        "Go Large Iced Tea" => 50m,
        _ => throw new ApiException(StatusCodes.Status400BadRequest, "Invalid selected choice.")
    };

    public static string? Normalize(string? choice) =>
        string.IsNullOrWhiteSpace(choice) ? null : choice.Trim();
}
