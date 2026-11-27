using System.Text.Json;
using FoodJS.Api.Common;
using FoodJS.Api.Contracts;
using FoodJS.Api.Models;

namespace FoodJS.Api.Services;

// One source of truth for menu display, cart validation and checkout pricing.
public static class MenuDrinkOptionRules
{
    public static IReadOnlyList<DrinkOptionResponse> GetOptions(MenuItem item) =>
        item.DrinkOptionsJson is null
            ? []
            : JsonSerializer.Deserialize<List<DrinkOptionResponse>>(item.DrinkOptionsJson)!;

    public static string ValidateAndSerialize(List<SaveDrinkOptionRequest?> options)
    {
        if (options.Count > 10) throw new ApiException(400, "A menu item can have at most 10 drink options.");
        var normalized = new List<DrinkOptionResponse>();
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in options)
        {
            var label = option?.Label?.Trim();
            if (string.IsNullOrEmpty(label) || label.Length > 80)
                throw new ApiException(400, "Each drink option needs a name of 1 to 80 characters.");
            if (!labels.Add(label)) throw new ApiException(400, "Drink option names must be unique.");
            if (option!.AddOn is not decimal price || price < 0 || price > 10_000 || decimal.Round(price, 2) != price)
                throw new ApiException(400, "Drink add-on prices must be between 0 and 10000, with up to two decimal places.");
            normalized.Add(new(label, price));
        }
        if (normalized.Count > 0 && normalized.Count(o => o.AddOn == 0m) != 1)
            throw new ApiException(400, "Choose exactly one included drink with an add-on price of 0.");
        return JsonSerializer.Serialize(normalized);
    }

    public static DrinkOptionResponse? Resolve(MenuItem item, string? requested)
    {
        var choice = ItemChoiceRules.Normalize(requested);
        var options = GetOptions(item);
        if (choice is null)
        {
            if (options.Count == 0) return null;
            throw new ApiException(400, $"Choose a drink for {item.Name}.");
        }
        // Exact labels keep checkout merging and cart identity consistent.
        return options.FirstOrDefault(o => o.Label == choice)
            ?? throw new ApiException(400, $"The selected drink is no longer available for {item.Name}. Please choose again.");
    }
}
