using FoodJS.Api.Models;

namespace FoodJS.Api.Repositories;

public sealed record StoredOrder(Order Order, IReadOnlyList<OrderItem> Items, Payment? Payment, IReadOnlyList<OrderStatusHistory> History);

public interface IOrderRepository
{
    Task<IReadOnlyList<StoredOrder>> LoadAsync(int? userId = null, int? orderId = null, CancellationToken ct = default);
    Task<Order?> FindAsync(int id, int? userId = null, CancellationToken ct = default);
    void Add(Order order);
    void AddItems(IEnumerable<OrderItem> items);
    void AddPayment(Payment payment);
    void AddHistory(OrderStatusHistory history);
    void AddNotification(Notification notification);
}
