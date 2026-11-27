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

// runs the real controllers against an in-memory SQLite database
public sealed class CartAndCategoryTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly FoodJsDbContext db;

    public CartAndCategoryTests()
    {
        connection.Open();
        db = new FoodJsDbContext(new DbContextOptionsBuilder<FoodJsDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();

        var role = new Role { Name = "customer" };
        db.Users.AddRange(
            new User { FirstName = "A", LastName = "A", Email = "a@x.test", PasswordHash = "", Role = role },
            new User { FirstName = "B", LastName = "B", Email = "b@x.test", PasswordHash = "", Role = role });
        var burgers = new Category { Name = "Burgers", Description = "d" };
        db.Categories.AddRange(burgers, new Category { Name = "Empty", Description = "d" });
        db.MenuItems.Add(new MenuItem { Name = "Burger", Price = 100, PrepTime = 5, Category = burgers, DrinkOptionsJson = TestMealDrinks.Json });
        db.SaveChanges();
    }

    [Fact]
    public async Task Category_with_menu_items_cannot_be_deleted_but_empty_one_can()
    {
        var categories = new CategoriesController(TestServices.Categories(db));

        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => categories.Delete("cat-1", default))).StatusCode);
        Assert.Equal(1, await db.MenuItems.CountAsync());
        Assert.IsType<OkObjectResult>(await categories.Delete("cat-2", default));
    }

    [Fact]
    public async Task Adding_same_item_twice_adds_quantity_and_carts_are_per_user()
    {
        var cartA = Cart(userId: 1);
        await cartA.Add(new AddCartItemRequest("M-1", 2, CartOptionPricing.RegularIcedTea), default);
        await cartA.Add(new AddCartItemRequest("M-1", 3, CartOptionPricing.RegularIcedTea), default);

        var items = Items(await cartA.GetMine(default));
        var item = Assert.Single(items);
        Assert.Equal(5, item.Quantity);
        Assert.Equal(100, item.Price);
        Assert.Equal(CartOptionPricing.RegularIcedTea, item.SelectedChoice);

        var cartB = Cart(userId: 2);
        Assert.Empty(Items(await cartB.GetMine(default)));
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => cartB.Remove(item.Id, default))).StatusCode);
        await cartB.Clear(default);
        Assert.Single(Items(await cartA.GetMine(default)));
    }

    private CartController Cart(int userId) => new(TestServices.Cart(db))
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test"))
            }
        }
    };

    private static List<CartItemResponse> Items(ActionResult<List<CartItemResponse>> result) =>
        Assert.IsType<List<CartItemResponse>>(Assert.IsType<OkObjectResult>(result.Result).Value);

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }
}
