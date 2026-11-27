using System.Net;
using System.Net.Http.Json;
using FoodJS.Api.Common;
using FoodJS.Api.Contracts;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class MenuDrinkOptionApiTests : IAsyncLifetime
{
    private readonly AssignedApiFactory factory = new();
    private HttpClient admin = null!;
    private HttpClient customer = null!;
    private HttpClient anonymous = null!;
    private int categoryId;
    private string legacyId = "";

    public async Task InitializeAsync()
    {
        anonymous = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        var owner = new User { FirstName = "Admin", LastName = "Test", Email = "admin@drink.test", PasswordHash = "unused", Role = new Role { Name = "admin" } };
        var buyer = new User { FirstName = "Customer", LastName = "Test", Email = "customer@drink.test", PasswordHash = "unused", Role = new Role { Name = "customer" } };
        var category = new Category { Name = "Meals" };
        var legacy = new MenuItem { Name = "Legacy", Price = 100m, PrepTime = 10, Category = category };
        db.AddRange(owner, buyer, legacy);
        await db.SaveChangesAsync();
        categoryId = category.Id;
        legacyId = PublicId.Menu(legacy.Id);
        admin = Client(owner.Id, "admin"); customer = Client(buyer.Id, "customer");
    }

    private HttpClient Client(int userId, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    public async Task DisposeAsync()
    {
        admin.Dispose(); customer.Dispose(); anonymous.Dispose();
        await factory.DisposeAsync();
    }

    private object Menu(object? options) => new
    {
        name = "Custom Meal", categoryId = PublicId.Category(categoryId), price = 100m, prepTime = 10,
        drinkOptions = options
    };
    private static object[] Options(decimal upgrade = 25.50m) =>
        [new { label = " Calamansi Juice ", addOn = 0m }, new { label = "Mango Shake", addOn = upgrade }];
    private async Task<MenuItemResponse> Create(object? options)
    {
        var result = await admin.PostAsJsonAsync("/api/menu", Menu(options));
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        return (await result.Content.ReadFromJsonAsync<MenuItemResponse>())!;
    }
    private static CreateOrderRequest Order(string id, string? choice) => new()
    {
        CustomerName = "Buyer", Items = [new() { MenuItemId = id, Quantity = 2, SelectedChoice = choice }]
    };

    [Fact]
    public async Task Custom_options_persist_and_drive_cart_checkout_and_historical_snapshots()
    {
        var created = await Create(Options());
        Assert.Equal("Calamansi Juice", created.DrinkOptions[0].Label);
        Assert.Equal(0m, created.DrinkOptions[0].AddOn);
        var reloaded = (await anonymous.GetFromJsonAsync<MenuItemResponse>($"/api/menu/{created.Id}"))!;
        Assert.Equal(created.DrinkOptions, reloaded.DrinkOptions);
        var page = (await anonymous.GetFromJsonAsync<MenuPageResponse>("/api/menu/paged"))!;
        Assert.Equal(25.50m, page.Items.Single(i => i.Id == created.Id).DrinkOptions[1].AddOn);

        // Browser-supplied prices are ignored; prices come from the saved configuration.
        var added = await customer.PostAsJsonAsync("/api/cart/items", new
            { menuItemId = created.Id, quantity = 2, selectedChoice = "Mango Shake", addOn = 0, price = 0.01m });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var cart = (await customer.GetFromJsonAsync<List<CartItemResponse>>("/api/cart"))!;
        Assert.Equal(125.50m, Assert.Single(cart).Price);

        var quoteResult = await customer.PostAsJsonAsync("/api/orders/quote", Order(created.Id, "Mango Shake"));
        Assert.Equal(HttpStatusCode.OK, quoteResult.StatusCode);
        Assert.Equal(251m, (await quoteResult.Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!.Total);
        var result = await customer.PostAsJsonAsync("/api/orders", Order(created.Id, "Mango Shake"));
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        var order = (await result.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal(251m, order.Total);

        var edited = await admin.PutAsJsonAsync($"/api/menu/{created.Id}", Menu(Options(40m)));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var newQuote = await customer.PostAsJsonAsync("/api/orders/quote", Order(created.Id, "Mango Shake"));
        Assert.Equal(280m, (await newQuote.Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!.Total);
        var saved = (await customer.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!;
        Assert.Equal(251m, saved.Total);
        Assert.Equal("Mango Shake", Assert.Single(saved.Items).SelectedChoice);
        Assert.Equal(125.50m, Assert.Single(saved.Items).Price);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Go Large Iced Tea")]
    [InlineData("MANGO SHAKE")]
    public async Task Configured_item_rejects_missing_or_unlisted_choices_in_cart_and_checkout(string? choice)
    {
        var item = await Create(Options());
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/cart/items",
            new { menuItemId = item.Id, quantity = 1, selectedChoice = choice })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/orders/quote", Order(item.Id, choice))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/orders", Order(item.Id, choice))).StatusCode);
    }

    [Fact]
    public async Task Included_drink_uses_base_price_and_removed_choices_cannot_be_checked_out()
    {
        var item = await Create(Options());
        var included = await customer.PostAsJsonAsync("/api/orders/quote", Order(item.Id, "Calamansi Juice"));
        Assert.Equal(200m, (await included.Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!.Total);
        await customer.PostAsJsonAsync("/api/cart/items", new { menuItemId = item.Id, quantity = 1, selectedChoice = "Mango Shake" });
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/menu/{item.Id}", Menu(new[] { new { label = "Calamansi Juice", addOn = 0m } }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/orders/quote", Order(item.Id, "Mango Shake"))).StatusCode);
    }

    [Fact]
    public async Task Changed_drink_price_invalidates_an_already_reviewed_checkout()
    {
        var item = await Create(Options());
        var quoteResult = await customer.PostAsJsonAsync("/api/orders/quote", Order(item.Id, "Mango Shake"));
        var quote = (await quoteResult.Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!;
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/menu/{item.Id}", Menu(Options(40m)))).StatusCode);
        var reviewed = new CreateOrderRequest
        {
            Items = Order(item.Id, "Mango Shake").Items, CustomerName = "Buyer", Address = "Cebu City",
            MobileNumber = "09171234567", PaymentMethod = "cash", QuoteToken = quote.QuoteToken,
            IdempotencyKey = Guid.NewGuid().ToString()
        };
        Assert.Equal(HttpStatusCode.Conflict, (await customer.PostAsJsonAsync("/api/orders", reviewed)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.Payments.ToListAsync());
    }

    [Fact]
    public async Task Empty_options_allow_items_without_drinks_and_cart_lines_merge()
    {
        var item = await Create(Array.Empty<object>());
        Assert.Empty(item.DrinkOptions);
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Created, (await customer.PostAsJsonAsync("/api/cart/items",
                new { menuItemId = item.Id, quantity = 1 })).StatusCode);
        var cart = (await customer.GetFromJsonAsync<List<CartItemResponse>>("/api/cart"))!;
        Assert.Equal(2, Assert.Single(cart).Quantity);
        Assert.Null(Assert.Single(cart).SelectedChoice);
        var quote = await customer.PostAsJsonAsync("/api/orders/quote", Order(item.Id, null));
        Assert.Equal(200m, (await quote.Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!.Total);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/cart/items",
            new { menuItemId = item.Id, quantity = 1, selectedChoice = "Mango Shake" })).StatusCode);
    }

    [Fact]
    public async Task Omitted_options_preserve_configuration_but_unconfigured_items_are_standalone()
    {
        var legacy = (await anonymous.GetFromJsonAsync<MenuItemResponse>($"/api/menu/{legacyId}"))!;
        Assert.Empty(legacy.DrinkOptions);
        Assert.Empty((await Create(null)).DrinkOptions);
        var item = await Create(Options());
        var update = await admin.PutAsJsonAsync($"/api/menu/{item.Id}", Menu(null));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(item.DrinkOptions, (await update.Content.ReadFromJsonAsync<MenuItemResponse>())!.DrinkOptions);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync("/api/menu", Menu(Options()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/menu", Menu(Options()))).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<FoodJsDbContext>().Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Turning_off_included_drinks_persists_and_rejects_old_choices_but_keeps_order_snapshots()
    {
        var item = await Create(Options());
        var placed = await customer.PostAsJsonAsync("/api/orders", Order(item.Id, "Mango Shake"));
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        var saved = (await placed.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/menu/{item.Id}", Menu(Array.Empty<object>()))).StatusCode);
        Assert.Empty((await anonymous.GetFromJsonAsync<MenuItemResponse>($"/api/menu/{item.Id}"))!.DrinkOptions);
        Assert.Equal(HttpStatusCode.Created, (await customer.PostAsJsonAsync("/api/cart/items",
            new { menuItemId = item.Id, quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/orders/quote", Order(item.Id, "Mango Shake"))).StatusCode);
        var history = (await customer.GetFromJsonAsync<OrderResponse>($"/api/orders/{saved.Id}"))!;
        Assert.Equal(saved.Total, history.Total);
        Assert.Equal("Mango Shake", Assert.Single(history.Items).SelectedChoice);
    }

    [Fact]
    public async Task Data_migration_normalizes_unconfigured_records_without_overwriting_explicit_bundles()
    {
        var configured = await Create(Options());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261127121246_AddAccountSessionVersion");
        Assert.Null((await db.MenuItems.AsNoTracking().SingleAsync(i => i.Name == "Legacy")).DrinkOptionsJson);
        await migrator.MigrateAsync(); db.ChangeTracker.Clear();
        Assert.Equal("[]", (await db.MenuItems.SingleAsync(i => i.Name == "Legacy")).DrinkOptionsJson);
        Assert.True(PublicId.TryParseMenu(configured.Id, out var configuredId));
        Assert.Equal(2, FoodJS.Api.Services.MenuDrinkOptionRules.GetOptions(
            await db.MenuItems.SingleAsync(i => i.Id == configuredId)).Count);
    }

    public static IEnumerable<object[]> InvalidOptions()
    {
        yield return [new object?[] { null }];
        yield return [new[] { new { label = "", addOn = 0m } }];
        yield return [new[] { new { label = new string('A', 81), addOn = 0m } }];
        yield return [new[] { new { label = "Tea", addOn = 0m }, new { label = " tea ", addOn = 1m } }];
        yield return [new[] { new { label = "Tea", addOn = -1m } }];
        yield return [new[] { new { label = "Tea", addOn = 10001m } }];
        yield return [new[] { new { label = "Tea", addOn = 0.001m } }];
        yield return [new[] { new { label = "Tea", addOn = 1m } }];
        yield return [new[] { new { label = "Tea", addOn = 0m }, new { label = "Juice", addOn = 0m } }];
        yield return [new[] { new { label = "Tea" } }];
        yield return [Enumerable.Range(0, 11).Select(i => new { label = $"Drink {i}", addOn = (decimal)i }).ToArray()];
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public async Task Invalid_options_are_rejected_on_create_and_update_without_overwriting_saved_options(object invalid)
    {
        var item = await Create(Options());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/menu", Menu(invalid))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/menu/{item.Id}", Menu(invalid))).StatusCode);
        var saved = (await anonymous.GetFromJsonAsync<MenuItemResponse>($"/api/menu/{item.Id}"))!;
        Assert.Equal(item.DrinkOptions, saved.DrinkOptions);
    }
}
