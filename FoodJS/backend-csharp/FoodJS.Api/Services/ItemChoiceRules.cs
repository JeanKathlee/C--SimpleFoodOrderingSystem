namespace FoodJS.Api.Services;

public static class ItemChoiceRules
{
    public static string? Normalize(string? choice) =>
        string.IsNullOrWhiteSpace(choice) ? null : choice.Trim();
}
