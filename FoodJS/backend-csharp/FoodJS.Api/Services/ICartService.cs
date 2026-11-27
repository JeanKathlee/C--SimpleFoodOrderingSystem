using FoodJS.Api.Contracts;
namespace FoodJS.Api.Services;
public interface ICartService
{
    Task<List<CartItemResponse>> GetMine(int userId, CancellationToken ct);
    Task Add(int userId, AddCartItemRequest request, CancellationToken ct);
    Task Update(int userId, int cartItemId, UpdateCartItemRequest request, CancellationToken ct);
    Task Remove(int userId, int cartItemId, CancellationToken ct);
    Task Clear(int userId, CancellationToken ct);
}
