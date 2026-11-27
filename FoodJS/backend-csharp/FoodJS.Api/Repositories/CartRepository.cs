using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;
namespace FoodJS.Api.Repositories;
public sealed class CartRepository(FoodJsDbContext db) : ICartRepository
{
    public Task<List<CartItem>> ListAsync(int userId, CancellationToken ct) => db.CartItems.AsNoTracking().Include(i => i.MenuItem).Where(i => i.ShoppingCart.UserId == userId).OrderBy(i => i.Id).ToListAsync(ct);
    public Task<MenuItem?> FindAvailableMenuAsync(int id, CancellationToken ct) => db.MenuItems.FirstOrDefaultAsync(i => i.Id == id && i.IsAvailable, ct);
    public Task<ShoppingCart?> FindCartAsync(int userId, CancellationToken ct) => db.ShoppingCarts.FirstOrDefaultAsync(c => c.UserId == userId, ct);
    public Task<CartItem?> FindMatchingAsync(int cartId, int menuId, string choice, CancellationToken ct) => db.CartItems.FirstOrDefaultAsync(i => i.ShoppingCartId == cartId && i.MenuItemId == menuId && i.SelectedChoice == choice, ct);
    public Task<CartItem?> FindOwnedAsync(int itemId, int userId, CancellationToken ct) => db.CartItems.Include(i => i.ShoppingCart).FirstOrDefaultAsync(i => i.Id == itemId && i.ShoppingCart.UserId == userId, ct);
    public Task<List<CartItem>> ListForCartAsync(int cartId, CancellationToken ct) => db.CartItems.IgnoreQueryFilters().Where(i => i.ShoppingCartId == cartId && !i.IsDeleted).ToListAsync(ct);
    public void AddCart(ShoppingCart cart) => db.ShoppingCarts.Add(cart);
    public void AddItem(CartItem item) => db.CartItems.Add(item);
}
