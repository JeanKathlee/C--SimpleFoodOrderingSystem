using FoodJS.Api.Controllers;
using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using FoodJS.Api.Data;
using FoodJS.Api.Repositories;
namespace FoodJS.Api.Tests;

// Explicit composition for tests that exercise real service/repository behavior.
internal static class TestServices
{
    public static OrderService Orders(FoodJsDbContext db) => new(new OrderRepository(db), new MenuRepository(db), new ReferenceDataRepository(db), new UnitOfWorkRepository(db));
    public static OrderStatusSeeder Statuses(FoodJsDbContext db) => new(new ReferenceDataRepository(db), new UnitOfWorkRepository(db));
    public static CartService Cart(FoodJsDbContext db) => new(new CartRepository(db), new UnitOfWorkRepository(db));
    public static CategoryService Categories(FoodJsDbContext db) => new(new CategoryRepository(db), new UnitOfWorkRepository(db));
}
