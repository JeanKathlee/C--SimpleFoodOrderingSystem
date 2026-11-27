using FoodJS.Api.Controllers;
using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using FoodJS.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Reflection;
using System.Security.Claims;
using Xunit;

namespace FoodJS.Api.Tests;

// Real migrations, services, repositories and relational constraints on isolated SQLite.
public sealed class AssignedRepositoryFlowTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private FoodJsDbContext db = null!;
    private MenuService menu = null!;
    private OrderService orders = null!;
    private IMenuRepository menuRepository = null!;
    private IOrderRepository orderRepository = null!;
    private IReferenceDataRepository references = null!;
    private IUnitOfWorkRepository unit = null!;
    private int menuId, categoryId, userId, otherUserId;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new FoodJsDbContext(new DbContextOptionsBuilder<FoodJsDbContext>().UseSqlite(connection).Options);
        unit = new UnitOfWorkRepository(db);
        await unit.MigrateAsync();
        menuRepository = new MenuRepository(db);
        orderRepository = new OrderRepository(db);
        references = new ReferenceDataRepository(db);
        menu = new MenuService(menuRepository, references, unit);
        orders = new OrderService(orderRepository, menuRepository, references, unit);
        var role = new Role { Name = "customer" };
        var user = new User { FirstName = "QA", LastName = "Customer", Email = "qa@example.invalid", PasswordHash = "unused", Role = role };
        var other = new User { FirstName = "Other", LastName = "Customer", Email = "other@example.invalid", PasswordHash = "unused", Role = role };
        var category = new Category { Name = "Burgers" };
        var item = new MenuItem { Name = "Burger", Description = "Original description", Price = 199.50m, PrepTime = 10,
            Image = "https://example.invalid/burger.png", Category = category };
        db.AddRange(user, other, item, new Coupon { Code = "SAVE10", Discount = 10, DiscountType = "percentage",
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10) });
        await db.SaveChangesAsync();
        menuId = item.Id; categoryId = category.Id; userId = user.Id; otherUserId = other.Id;
        await new OrderStatusSeeder(references, unit).EnsureSeededAsync();
        db.ChangeTracker.Clear();
    }

    private CreateOrderRequest Checkout() => new()
    {
        Items = [new() { MenuItemId = PublicId.Menu(menuId), Quantity = 3 }], CustomerName = "QA Recipient",
        Address = "QA street, Manila", MobileNumber = "09171234567", PaymentMethod = "cash", CouponCode = "SAVE10"
    };
    private SaveMenuItemRequest Edit(bool? available = null) => new()
    { Name = "Edited burger", CategoryId = PublicId.Category(categoryId), Price = 219.50m, PrepTime = 12, IsAvailable = available };

    [Fact]
    public async Task Menu_visibility_search_and_updates_preserve_optional_fields()
    {
        await menu.SetAvailabilityAsync(menuId, false);
        Assert.Empty(await menu.GetAllAsync(false));
        Assert.Null(await menu.GetAsync(menuId, false));
        Assert.Single(await menu.GetAllAsync(true, "  BURGER ", " BURGERS "));
        var updated = (await menu.UpdateAsync(menuId, Edit()))!;
        Assert.False(updated.IsAvailable);
        Assert.Equal("Original description", updated.Description);
        Assert.Equal("https://example.invalid/burger.png", updated.Image);
        Assert.Equal(219.50m, updated.Price);
        await menu.SetAvailabilityAsync(menuId, true);
        Assert.Single(await menu.GetAllAsync(false));
    }

    [Fact]
    public async Task Menu_validation_remains_in_service_and_never_saves_invalid_items()
    {
        var error = await Assert.ThrowsAsync<ApiException>(() => menu.CreateAsync(new() { Name = " ", Price = 1, PrepTime = 1, CategoryId = PublicId.Category(categoryId) }, null));
        Assert.Equal(400, error.StatusCode);
        await Assert.ThrowsAsync<ApiException>(() => menu.CreateAsync(new() { Name = "Bad image", Price = 1, PrepTime = 1,
            CategoryId = PublicId.Category(categoryId), Image = "data:image/png;base64,AAAA" }, null));
        await Assert.ThrowsAsync<ApiException>(() => menu.CreateAsync(new() { Name = "Bad category", Price = 1, PrepTime = 1, CategoryId = "cat-9999" }, null));
        await Assert.ThrowsAsync<ApiException>(() => menu.SetAvailabilityAsync(menuId, null));
        Assert.Equal(1, await db.MenuItems.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Ordered_menu_item_can_be_soft_deleted_without_erasing_history_or_cart_rows()
    {
        var order = await orders.CreateAsync(userId, Checkout());
        db.CartItems.Add(new CartItem { MenuItemId = menuId, Quantity = 2, ShoppingCart = new ShoppingCart { UserId = userId } });
        await db.SaveChangesAsync();
        Assert.True(await menu.DeleteAsync(menuId));
        Assert.True((await db.MenuItems.IgnoreQueryFilters().SingleAsync()).IsDeleted);
        Assert.Empty(await menu.GetAllAsync(true));
        Assert.Empty(await menu.GetAllAsync(false));
        Assert.Null(await menu.GetAsync(menuId, true));
        Assert.Null(await menu.UpdateAsync(menuId, Edit()));
        Assert.Null(await menu.SetAvailabilityAsync(menuId, true));
        Assert.False(await menu.DeleteAsync(menuId));
        var saved = (await orders.GetForUserAsync(userId, order.Id))!;
        Assert.Equal(199.50m, Assert.Single(saved.Items).Price);
        Assert.Equal("Burger", saved.Items[0].Name);
        Assert.Equal(538.65m, saved.Total);
        var failure = await Assert.ThrowsAsync<ApiException>(() => orders.CreateAsync(userId, Checkout()));
        Assert.Equal(400, failure.StatusCode);
        Assert.Equal(1, await db.Orders.CountAsync());
        Assert.Empty(Assert.IsType<List<CartItemResponse>>(Assert.IsType<OkObjectResult>((await Cart().GetMine(default)).Result).Value));
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => Cart().Add(new(PublicId.Menu(menuId), 1, CartOptionPricing.RegularIcedTea), default))).StatusCode);
        Assert.Equal(1, await db.CartItems.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Order_soft_delete_hides_all_normal_access_but_retains_every_related_record()
    {
        var order = await orders.CreateAsync(userId, Checkout());
        Assert.True(await orders.DeleteAsync(order.Id));
        Assert.Empty(await orders.ListForUserAsync(userId));
        Assert.Empty(await orders.ListAllAsync());
        Assert.Null(await orders.GetForUserAsync(userId, order.Id));
        Assert.Null(await orders.CancelAsync(userId, order.Id));
        Assert.Null(await orders.UpdateStatusAsync(userId, order.Id, new() { Status = "Preparing" }));
        Assert.False(await orders.DeleteAsync(order.Id));
        Assert.True((await db.Orders.IgnoreQueryFilters().SingleAsync()).IsDeleted);
        Assert.Equal(1, await db.OrderItems.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await db.Payments.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await db.OrderStatusHistories.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await db.Addresses.CountAsync());
        Assert.NotNull((await db.Notifications.SingleAsync()).OrderId);
        Assert.Equal(1, await db.MenuItems.CountAsync());
    }

    [Fact]
    public async Task Order_ownership_pricing_and_status_rules_survive_repository_refactor()
    {
        var created = await orders.CreateAsync(userId, Checkout());
        Assert.Equal(598.50m, created.Subtotal); Assert.Equal(59.85m, created.Discount); Assert.Equal(538.65m, created.Total);
        Assert.Equal(3, created.ItemCount);
        Assert.Empty(await orders.ListForUserAsync(otherUserId));
        Assert.Null(await orders.GetForUserAsync(otherUserId, created.Id));
        Assert.Null(await orders.CancelAsync(otherUserId, created.Id));
        await Assert.ThrowsAsync<ApiException>(() => orders.UpdateStatusAsync(userId, created.Id, new() { Status = "Delivered" }));
        foreach (var status in new[] { "Preparing", "Ready", "Delivered" })
            Assert.Equal(status, (await orders.UpdateStatusAsync(userId, created.Id, new() { Status = status }))!.Status);
        var saved = (await orders.GetForUserAsync(userId, created.Id))!;
        Assert.Equal(4, saved.StatusHistory.Count);
        Assert.Equal(4, await db.Notifications.CountAsync());
        await Assert.ThrowsAsync<ApiException>(() => orders.CancelAsync(userId, created.Id));
        Assert.False((await db.Orders.SingleAsync()).IsDeleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_is_a_status_change_not_a_soft_delete(bool preparing)
    {
        var created = await orders.CreateAsync(userId, Checkout());
        if (preparing) await orders.UpdateStatusAsync(userId, created.Id, new() { Status = "Preparing" });
        Assert.Equal("Cancelled", (await orders.CancelAsync(userId, created.Id))!.Status);
        Assert.Single(await orders.ListForUserAsync(userId));
        Assert.False((await db.Orders.SingleAsync()).IsDeleted);
    }

    [Fact]
    public async Task Checkout_rolls_back_first_save_and_all_side_effects_if_second_save_fails()
    {
        var failing = new OrderService(orderRepository, menuRepository, references, new FailSecondSave(unit));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.CreateAsync(userId, Checkout()));
        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.Orders.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.OrderItems.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.Payments.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.OrderStatusHistories.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.Notifications.CountAsync()); Assert.Equal(0, await db.Addresses.CountAsync());
        Assert.Equal(0, (await db.Coupons.SingleAsync()).UsedCount);
        Assert.Null((await db.Users.SingleAsync(user => user.Id == userId)).MobileNumber);
    }

    [Theory]
    [InlineData("Categories")]
    [InlineData("Users")]
    [InlineData("Orders")]
    [InlineData("MenuItems")]
    public async Task Database_constraints_block_physical_parent_deletes_even_for_soft_deleted_records(string table)
    {
        var order = await orders.CreateAsync(userId, Checkout());
        await orders.DeleteAsync(order.Id); await menu.DeleteAsync(menuId);
        var id = table switch { "Categories" => categoryId, "Users" => userId, "Orders" => int.Parse(order.Id[2..]), _ => menuId };
        var sql = table switch {
            "Categories" => "DELETE FROM Categories WHERE Id = {0}",
            "Users" => "DELETE FROM Users WHERE Id = {0}",
            "Orders" => "DELETE FROM Orders WHERE Id = {0}",
            _ => "DELETE FROM MenuItems WHERE Id = {0}"
        };
        var failure = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql, id));
        Assert.Equal(19, failure.SqliteErrorCode);
        Assert.Equal(1, await db.Orders.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await db.OrderItems.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Startup_does_not_reseed_when_every_menu_item_was_soft_deleted()
    {
        await menu.DeleteAsync(menuId);
        await menu.SeedDevelopmentDataAsync("This directory intentionally does not exist");
        Assert.Equal(1, await db.MenuItems.IgnoreQueryFilters().CountAsync());
        await new OrderStatusSeeder(references, unit).EnsureSeededAsync();
        Assert.Equal(5, await db.OrderStatuses.CountAsync());
    }

    [Fact]
    public async Task Migration_preserves_legacy_menu_and_order_graph_and_defaults_new_flags_to_false()
    {
        var created = await orders.CreateAsync(userId, Checkout());
        db.ChangeTracker.Clear();
        // Put complete records on the previous schema, then exercise the real upgrade.
        await db.GetService<IMigrator>().MigrateAsync("20261119155556_AddCartItemSelectedChoice");
        var oldColumns = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Orders')").ToListAsync();
        Assert.DoesNotContain("IsDeleted", oldColumns);
        await unit.MigrateAsync();
        Assert.False((await db.MenuItems.SingleAsync()).IsDeleted);
        Assert.False((await db.Orders.SingleAsync()).IsDeleted);
        var restored = (await orders.GetForUserAsync(userId, created.Id))!;
        Assert.Equal(created.Total, restored.Total);
        Assert.Equal(created.Address, restored.Address);
        Assert.Equal(199.50m, Assert.Single(restored.Items).Price);
        Assert.Single(restored.StatusHistory);
        Assert.Equal(1, await db.Payments.CountAsync());
        Assert.Equal(1, await db.Notifications.CountAsync());
    }

    [Fact]
    public void Assigned_controllers_services_and_repository_contracts_keep_their_layer_boundaries()
    {
        foreach (var type in new[] { typeof(MenuController), typeof(OrdersController), typeof(MenuService), typeof(OrderService), typeof(OrderStatusSeeder) })
        {
            foreach (var argument in type.GetConstructors().SelectMany(constructor => constructor.GetParameters()))
                Assert.NotEqual(typeof(FoodJsDbContext), argument.ParameterType);
        }
        foreach (var type in new[] { typeof(IMenuRepository), typeof(IOrderRepository), typeof(IReferenceDataRepository), typeof(IUnitOfWorkRepository) })
            Assert.DoesNotContain(type.GetMethods(), method => method.ReturnType.Name.Contains("IQueryable"));
        Assert.NotNull(typeof(OrdersController).GetCustomAttribute<AuthorizeAttribute>());
        foreach (var name in new[] { "Create", "Update", "SetAvailability", "Delete" })
            Assert.Equal("admin", typeof(MenuController).GetMethod(name)!.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        foreach (var name in new[] { "GetAll", "UpdateStatus", "Delete" })
            Assert.Equal("admin", typeof(OrdersController).GetMethod(name)!.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
    }

    private CartController Cart() => new(TestServices.Cart(db)) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
    { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test")) } } };

    private sealed class FailSecondSave(IUnitOfWorkRepository inner) : IUnitOfWorkRepository
    {
        private int saves;
        public Task SaveAsync(CancellationToken ct = default) => ++saves == 2 ? throw new InvalidOperationException("Injected second-save failure") : inner.SaveAsync(ct);
        public Task<IRepositoryTransaction> BeginTransactionAsync(CancellationToken ct = default) => inner.BeginTransactionAsync(ct);
        public Task MigrateAsync(CancellationToken ct = default) => inner.MigrateAsync(ct);
    }

    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
}
