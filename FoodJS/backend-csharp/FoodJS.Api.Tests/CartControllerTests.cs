using FoodJS.Api.Controllers;
using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using System.Security.Claims;
using FoodJS.Api.Data;
using FoodJS.Api.Common;
using FoodJS.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class CartControllerTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly FoodJsDbContext db;

    public CartControllerTests()
    {
        connection.Open();
        db = new FoodJsDbContext(
            new DbContextOptionsBuilder<FoodJsDbContext>()
                .UseSqlite(connection)
                .Options);
        db.Database.EnsureCreated();

        var customerRole = new Role { Name = "customer" };
        db.Users.AddRange(
            new User
            {
                FirstName = "First",
                LastName = "Customer",
                Email = "first@example.test",
                PasswordHash = "test",
                Role = customerRole
            },
            new User
            {
                FirstName = "Second",
                LastName = "Customer",
                Email = "second@example.test",
                PasswordHash = "test",
                Role = customerRole
            });

        var category = new Category { Name = "Meals" };
        db.MenuItems.AddRange(
            new MenuItem
            {
                Name = "Burger Meal",
                Price = 100m,
                PrepTime = 10,
                IsAvailable = true,
                Category = category
            },
            new MenuItem
            {
                Name = "Unavailable Meal",
                Price = 80m,
                PrepTime = 10,
                IsAvailable = false,
                Category = category
            });
        db.SaveChanges();
    }

    [Fact]
    public async Task Add_and_list_use_server_option_prices_and_keep_choices_separate()
    {
        var controller = ControllerFor(userId: 1);

        Assert.IsType<ObjectResult>(await controller.Add(
            new AddCartItemRequest("M-1", 2, CartOptionPricing.LargeIcedTea),
            default));
        Assert.IsType<ObjectResult>(await controller.Add(
            new AddCartItemRequest("M-1", 1, CartOptionPricing.RegularIcedTea),
            default));

        var items = await ListFor(controller);
        Assert.Equal(2, items.Count);

        var large = Assert.Single(items, item => item.SelectedChoice == CartOptionPricing.LargeIcedTea);
        Assert.Equal(150m, large.Price);
        Assert.Equal(2, large.Quantity);

        var regular = Assert.Single(items, item => item.SelectedChoice == CartOptionPricing.RegularIcedTea);
        Assert.Equal(100m, regular.Price);
        Assert.Equal(1, regular.Quantity);
    }

    [Fact]
    public async Task Add_same_menu_item_and_choice_combines_quantity()
    {
        var controller = ControllerFor(userId: 1);

        await controller.Add(new AddCartItemRequest("M-1", 2, CartOptionPricing.MediumIcedTea), default);
        await controller.Add(new AddCartItemRequest("M-1", 3, CartOptionPricing.MediumIcedTea), default);

        var item = Assert.Single(await ListFor(controller));
        Assert.Equal(5, item.Quantity);
        Assert.Equal(145m, item.Price);
    }

    [Fact]
    public async Task Update_and_remove_target_a_cart_line_and_enforce_ownership()
    {
        var owner = ControllerFor(userId: 1);
        await owner.Add(new AddCartItemRequest("M-1", 1, CartOptionPricing.RegularIcedTea), default);
        var item = Assert.Single(await ListFor(owner));

        var otherUser = ControllerFor(userId: 2);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => otherUser.Update(item.Id, new UpdateCartItemRequest(4), default))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => otherUser.Remove(item.Id, default))).StatusCode);

        Assert.IsType<OkObjectResult>(
            await owner.Update(item.Id, new UpdateCartItemRequest(4), default));
        Assert.Equal(4, Assert.Single(await ListFor(owner)).Quantity);

        Assert.IsType<OkObjectResult>(await owner.Remove(item.Id, default));
        Assert.Empty(await ListFor(owner));
    }

    [Fact]
    public async Task Clear_removes_only_the_current_users_items()
    {
        var firstUser = ControllerFor(userId: 1);
        var secondUser = ControllerFor(userId: 2);
        await firstUser.Add(new AddCartItemRequest("M-1", 1, CartOptionPricing.RegularIcedTea), default);
        await secondUser.Add(new AddCartItemRequest("M-1", 1, CartOptionPricing.RegularIcedTea), default);

        Assert.IsType<OkObjectResult>(await firstUser.Clear(default));

        Assert.Empty(await ListFor(firstUser));
        Assert.Single(await ListFor(secondUser));
    }

    [Fact]
    public async Task Add_rejects_invalid_quantity_choice_and_unavailable_items()
    {
        var controller = ControllerFor(userId: 1);

        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => controller.Add(
            new AddCartItemRequest("M-1", 0, null),
            default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => controller.Add(
            new AddCartItemRequest("M-1", 1, null),
            default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => controller.Add(
            new AddCartItemRequest("M-1", 1, "Fake option"),
            default))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => controller.Add(
            new AddCartItemRequest("M-2", 1, CartOptionPricing.RegularIcedTea),
            default))).StatusCode);
    }

    private CartController ControllerFor(int userId) => new(TestServices.Cart(db))
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim("sub", userId.ToString())], "test"))
            }
        }
    };

    private static async Task<List<CartItemResponse>> ListFor(CartController controller)
    {
        var action = await controller.GetMine(default);
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        return Assert.IsType<List<CartItemResponse>>(ok.Value);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }
}
