using FoodJS.Api.Contracts;
namespace FoodJS.Api.Services;

/// <summary>
/// Server-owned prices for the drink choices shown by ProductDetails.jsx.
/// The browser sends only the selected label; it never decides the price.
/// </summary>
public static class CartOptionPricing
{
    public const string RegularIcedTea = "Go Regular Iced Tea";
    public const string MediumIcedTea = "Go Medium Iced Tea";
    public const string LargeIcedTea = "Go Large Iced Tea";

    public static bool TryResolve(
        string? requestedChoice,
        out string? selectedChoice,
        out decimal addOn)
    {
        selectedChoice = null;
        addOn = 0m;

        if (string.IsNullOrWhiteSpace(requestedChoice))
        {
            return false;
        }

        var choice = requestedChoice.Trim();
        if (choice.Equals(RegularIcedTea, StringComparison.OrdinalIgnoreCase))
        {
            selectedChoice = RegularIcedTea;
            return true;
        }

        if (choice.Equals(MediumIcedTea, StringComparison.OrdinalIgnoreCase))
        {
            selectedChoice = MediumIcedTea;
            addOn = 45m;
            return true;
        }

        if (choice.Equals(LargeIcedTea, StringComparison.OrdinalIgnoreCase))
        {
            selectedChoice = LargeIcedTea;
            addOn = 50m;
            return true;
        }

        return false;
    }
}
