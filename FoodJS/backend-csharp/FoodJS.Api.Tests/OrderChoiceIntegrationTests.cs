using FoodJS.Api.Controllers;
using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using System.Text.Json;
using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class OrderChoiceIntegrationTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private FoodJsDbContext db = null!;
    private int userId;
    private int menuId;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new FoodJsDbContext(new DbContextOptionsBuilder<FoodJsDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.MigrateAsync();
        await TestServices.Statuses(db).EnsureSeededAsync();

        var role = new Role { Name = "customer" };
        var user = new User
        {
            FirstName = "Test", LastName = "Customer", Email = "test@example.invalid",
            PasswordHash = "unused", Role = role
        };
        var item = new MenuItem
        {
            Name = "Burger", Price = 100m, PrepTime = 10,
            DrinkOptionsJson = TestMealDrinks.Json,
            Category = new Category { Name = "Burgers" }
        };
        db.AddRange(user, item);
        await db.SaveChangesAsync();
        userId = user.Id;
        menuId = item.Id;
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Upgrade_uses_server_price_and_survives_reload_and_menu_price_changes()
    {
        // The browser sends price/name fields, but neither can override server pricing.
        var json = $$"""
            {"customerName":"Test Customer","items":[
              {"menuItemId":"M-{{menuId}}","quantity":2,"selectedChoice":"Go Large Iced Tea",
               "price":0.01,"addOn":0,"name":"Tampered name"}]}
            """;
        var request = JsonSerializer.Deserialize<CreateOrderRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var service = TestServices.Orders(db);
        var created = await service.CreateAsync(userId, request);

        Assert.Equal(300m, created.Subtotal);
        Assert.Equal(300m, created.Total);
        var line = Assert.Single(created.Items);
        Assert.Equal(150m, line.Price);
        Assert.Equal("Burger", line.Name);
        Assert.Equal("Go Large Iced Tea", line.SelectedChoice);
        Assert.Equal(300m, (await db.Payments.SingleAsync()).Amount);

        (await db.MenuItems.SingleAsync()).Price = 999m;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var reloaded = await service.GetForUserAsync(userId, created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(300m, reloaded.Total);
        Assert.Equal(150m, Assert.Single(reloaded.Items).Price);
        Assert.Equal("Go Large Iced Tea", Assert.Single(reloaded.Items).SelectedChoice);
    }

    [Fact]
    public async Task Different_choices_remain_separate_and_matching_choices_merge()
    {
        var result = await TestServices.Orders(db).CreateAsync(userId, Request(
            Line("Go Regular Iced Tea"), Line("Go Large Iced Tea"),
            Line(" Go Large Iced Tea ", 2), Line("Go Medium Iced Tea")));

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(5, result.ItemCount);
        Assert.Equal(695m, result.Total); // 100 + (150 * 3) + 145.
        Assert.Equal(3, result.Items.Single(item => item.SelectedChoice == "Go Large Iced Tea").Quantity);
        Assert.Equal(3, await db.OrderItems.CountAsync());
    }

    [Fact]
    public async Task Coupons_apply_to_the_total_including_upgrades()
    {
        db.Coupons.Add(new Coupon
        {
            Code = "SAVE10", DiscountType = "percentage", Discount = 10m,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
        });
        await db.SaveChangesAsync();
        var result = await TestServices.Orders(db).CreateAsync(userId, new CreateOrderRequest
        {
            CustomerName = "Test Customer", CouponCode = "SAVE10",
            Items = [Line("Go Medium Iced Tea", 2)]
        });
        Assert.Equal(290m, result.Subtotal);
        Assert.Equal(29m, result.Discount);
        Assert.Equal(261m, result.Total);
    }

    [Theory]
    [InlineData("Go Regular Iced Tea")]
    public async Task No_upgrade_keeps_the_base_price(string? choice)
    {
        var result = await TestServices.Orders(db).CreateAsync(userId, Request(Line(choice, 2)));
        Assert.Equal(200m, result.Total);
        Assert.Equal(100m, Assert.Single(result.Items).Price);
    }

    [Theory]
    [InlineData("Free Large Iced Tea")]
    [InlineData("go large iced tea")]
    public async Task Unknown_choices_are_rejected_without_saving_an_order(string choice)
    {
        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            TestServices.Orders(db).CreateAsync(userId, Request(Line(choice))));
        Assert.Equal(400, exception.StatusCode);
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.Payments.ToListAsync());
    }

    [Fact]
    public async Task Quantity_overflow_is_rejected_before_saving()
    {
        var exception = await Assert.ThrowsAsync<ApiException>(() => TestServices.Orders(db).CreateAsync(
            userId, Request(Line("Go Large Iced Tea", int.MaxValue), Line("Go Large Iced Tea"))));
        Assert.Equal(400, exception.StatusCode);
        Assert.Empty(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Migration_preserves_existing_order_lines_without_a_choice()
    {
        var order = new Order
        {
            UserId = userId, OrderStatusId = (await db.OrderStatuses.FirstAsync()).Id, TotalAmount = 100m
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        // Preserve an existing order while recreating the schema before choice/snapshot columns.
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261028160625_InitialCreate");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO OrderItems (OrderId, MenuItemId, ItemName, Quantity, SnapshotPrice, LineTotal)
            VALUES ({order.Id}, {menuId}, {"Burger"}, {1}, {100m}, {100m})
            """);

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        var line = await db.OrderItems.SingleAsync();
        Assert.Null(line.SelectedChoice);
        Assert.Equal(100m, line.SnapshotPrice);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private CreateOrderItem Line(string? choice, int quantity = 1) => new()
    {
        MenuItemId = PublicId.Menu(menuId), Quantity = quantity, SelectedChoice = choice
    };

    private static CreateOrderRequest Request(params CreateOrderItem[] items) => new()
    {
        CustomerName = "Test Customer", Items = items.ToList()
    };
}
