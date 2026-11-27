using FoodJS.Api.Contracts;

namespace FoodJS.Api.Tests;

// Tests of meal upgrades opt in explicitly; unconfigured items are standalone.
internal static class TestMealDrinks
{
    public const string Json = "[{\"Label\":\"Go Large Iced Tea\",\"AddOn\":50},{\"Label\":\"Go Medium Iced Tea\",\"AddOn\":45},{\"Label\":\"Go Regular Iced Tea\",\"AddOn\":0}]";
    public static SaveDrinkOptionRequest[] Options =>
        [new("Go Large Iced Tea", 50m), new("Go Medium Iced Tea", 45m), new("Go Regular Iced Tea", 0m)];
}
