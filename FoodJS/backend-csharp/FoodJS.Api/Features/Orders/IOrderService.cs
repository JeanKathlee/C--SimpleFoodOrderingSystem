namespace FoodJS.Api.Features.Orders;

public interface IOrderService
{
    // Every order for a given user, newest first.
    Task<IReadOnlyList<OrderResponse>> ListForUserAsync(int userId, CancellationToken ct = default);

    // All orders in the system, newest first. Admin only.
    Task<IReadOnlyList<OrderResponse>> ListAllAsync(CancellationToken ct = default);

    // The order for a given user, or null if it does not exist or belongs to someone else.
    Task<OrderResponse?> GetForUserAsync(int userId, string publicId, CancellationToken ct = default);

    Task<OrderResponse> CreateAsync(int userId, CreateOrderRequest request, CancellationToken ct = default);

    // Cancel the order for a given user. Null when the order does not exist or belongs to someone else.
    Task<OrderResponse?> CancelAsync(int userId, string publicId, CancellationToken ct = default);

    // Admin update of the order status. Null when the order does not exist.
    Task<OrderResponse?> UpdateStatusAsync(
        int adminId, string publicId, UpdateOrderStatusRequest request, CancellationToken ct = default);

    // Admin update of the order items. Null when the order does not exist.
    Task<bool> DeleteAsync(string publicId, CancellationToken ct = default);
}
