using FoodJS.Api.Controllers;
using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class OrderSnapshotIntegrationTests : IAsyncLifetime
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

        var user = new User
        {
            FirstName = "Profile", LastName = "Owner", MobileNumber = "09170000000",
            Email = "snapshot@example.invalid", PasswordHash = "unused",
            Role = new Role { Name = "customer" }
        };
        var item = new MenuItem
        {
            Name = "Burger", Price = 100m, PrepTime = 10,
            Category = new Category { Name = "Burgers" }
        };
        db.AddRange(user, item, new Coupon
        {
            Code = "SAVE10", DiscountType = "percentage", Discount = 10m,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
        });
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
    public async Task Submitted_details_survive_profile_edits_and_coupon_rename_or_deletion()
    {
        var service = TestServices.Orders(db);
        var created = await service.CreateAsync(userId, Request("  Delivery Recipient  ", "  09990000000  ", " SAVE10 "));
        AssertDetails(created, "Delivery Recipient", "09990000000", "SAVE10");
        Assert.Equal(90m, created.Total);
        var saved = await db.Orders.SingleAsync();
        Assert.Equal("Delivery Recipient", saved.CustomerName);
        Assert.Equal("09990000000", saved.MobileNumber);
        Assert.Equal("SAVE10", saved.CouponCode);

        var user = await db.Users.SingleAsync();
        Assert.Equal("09170000000", user.MobileNumber); // Checkout does not replace an existing profile number.
        user.FirstName = "Changed";
        user.LastName = "Profile";
        user.MobileNumber = "09220000000";
        var coupon = await db.Coupons.SingleAsync();
        coupon.Code = "RENAMED";
        coupon.Discount = 75m;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        AssertDetails((await service.GetForUserAsync(userId, created.Id))!, "Delivery Recipient", "09990000000", "SAVE10");
        AssertDetails(Assert.Single(await service.ListForUserAsync(userId)), "Delivery Recipient", "09990000000", "SAVE10");
        AssertDetails(Assert.Single(await service.ListAllAsync()), "Delivery Recipient", "09990000000", "SAVE10");

        db.Coupons.Remove(await db.Coupons.SingleAsync());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var reloaded = (await service.GetForUserAsync(userId, created.Id))!;
        AssertDetails(reloaded, "Delivery Recipient", "09990000000", "SAVE10");
        Assert.Equal(10m, reloaded.Discount);
        Assert.Equal(90m, reloaded.Total);
    }

    [Fact]
    public async Task Each_order_keeps_its_own_contact_details_and_coupon()
    {
        var service = TestServices.Orders(db);
        var first = await service.CreateAsync(userId, Request("First Recipient", "09111111111", "SAVE10"));
        var second = await service.CreateAsync(userId, Request("Second Recipient", "09222222222"));
        db.ChangeTracker.Clear();
        AssertDetails((await service.GetForUserAsync(userId, first.Id))!, "First Recipient", "09111111111", "SAVE10");
        AssertDetails((await service.GetForUserAsync(userId, second.Id))!, "Second Recipient", "09222222222", null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_optional_contact_and_coupon_do_not_fall_back_to_live_profile(string? empty)
    {
        var service = TestServices.Orders(db);
        var created = await service.CreateAsync(userId, Request("Recipient", empty, empty));
        db.ChangeTracker.Clear();
        AssertDetails((await service.GetForUserAsync(userId, created.Id))!, "Recipient", string.Empty, null);
        Assert.Equal("09170000000", (await db.Users.SingleAsync()).MobileNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("09170000000")]
    public async Task Migration_backfills_legacy_contacts_once_and_leaves_unknown_coupon_null(string? legacyMobile)
    {
        var user = await db.Users.SingleAsync();
        user.MobileNumber = legacyMobile;
        await db.SaveChangesAsync();
        var service = TestServices.Orders(db);
        var created = await service.CreateAsync(userId, Request("Old Recipient", legacyMobile, "SAVE10"));

        // Dropping the snapshot columns recreates the previous schema: original checkout details are unavailable.
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261120072814_PreserveItemChoices");
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        var migrated = (await service.GetForUserAsync(userId, created.Id))!;
        AssertDetails(migrated, "Profile Owner", legacyMobile ?? string.Empty, null);
        Assert.Equal(90m, migrated.Total);

        user = await db.Users.SingleAsync();
        user.FirstName = "Changed";
        user.MobileNumber = "09333333333";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        AssertDetails((await service.GetForUserAsync(userId, created.Id))!, "Profile Owner", legacyMobile ?? string.Empty, null);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Invalid_coupon_is_not_saved_as_an_applied_coupon()
    {
        var error = await Assert.ThrowsAsync<ApiException>(() =>
            TestServices.Orders(db).CreateAsync(userId, Request("Recipient", "09111111111", "BADCODE")));
        Assert.Equal(400, error.StatusCode);
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Equal(0, (await db.Coupons.SingleAsync()).UsedCount);
    }

    private CreateOrderRequest Request(string name, string? mobile, string? coupon = null) => new()
    {
        CustomerName = name, MobileNumber = mobile, CouponCode = coupon,
        Items = [new CreateOrderItem { MenuItemId = PublicId.Menu(menuId), Quantity = 1 }]
    };

    private static void AssertDetails(OrderResponse order, string name, string mobile, string? coupon)
    {
        Assert.NotNull(order);
        Assert.Equal(name, order.CustomerName);
        Assert.Equal(mobile, order.MobileNumber);
        Assert.Equal(coupon, order.CouponCode);
    }
}
