using FoodJS.Api.Models;
namespace FoodJS.Api.Repositories;
public interface ICartRepository
{
    Task<List<CartItem>> ListAsync(int userId, CancellationToken ct);
    Task<MenuItem?> FindAvailableMenuAsync(int id, CancellationToken ct);
    Task<ShoppingCart?> FindCartAsync(int userId, CancellationToken ct);
    Task<CartItem?> FindMatchingAsync(int cartId, int menuId, string? choice, CancellationToken ct);
    Task<CartItem?> FindOwnedAsync(int itemId, int userId, CancellationToken ct);
    Task<List<CartItem>> ListForCartAsync(int cartId, CancellationToken ct);
    void AddCart(ShoppingCart cart);
    void AddItem(CartItem item);
}
