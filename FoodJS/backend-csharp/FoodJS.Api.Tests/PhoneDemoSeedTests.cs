using FoodJS.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class PhoneDemoSeedTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Catalog_opt_in_never_creates_users_or_orders(bool enabled)
    {
        await using var factory = new AssignedApiFactory();
        factory.Settings["Demo:SeedCatalog"] = enabled.ToString();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        Assert.Equal(enabled, await db.MenuItems.AnyAsync());
        Assert.Equal(enabled, await db.Categories.AnyAsync());
        Assert.Equal(enabled, await db.Coupons.AnyAsync());
        Assert.False(await db.Users.AnyAsync());
        Assert.False(await db.Orders.AnyAsync());
        if (enabled)
        {
            var items = await db.MenuItems.Include(i => i.Category).ToListAsync();
            Assert.All(items, item => Assert.NotNull(item.DrinkOptionsJson));
            Assert.All(items.Where(item => item.Category.Name != "Burgers"), item =>
                Assert.Equal("[]", item.DrinkOptionsJson));
            Assert.All(items.Where(item => item.Category.Name == "Burgers"), item =>
                Assert.Equal(3, FoodJS.Api.Services.MenuDrinkOptionRules.GetOptions(item).Count));
        }
    }
}
