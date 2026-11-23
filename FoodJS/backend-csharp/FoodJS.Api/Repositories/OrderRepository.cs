using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Repositories;

public sealed class OrderRepository(FoodJsDbContext db) : IOrderRepository
{
    public async Task<IReadOnlyList<StoredOrder>> LoadAsync(int? userId = null, int? orderId = null, CancellationToken ct = default)
    {
        var query = db.Orders.AsNoTracking();
        if (userId is not null) query = query.Where(order => order.UserId == userId);
        if (orderId is not null) query = query.Where(order => order.Id == orderId);
        var orders = await query.Include(order => order.User).Include(order => order.OrderStatus).Include(order => order.Address).ToListAsync(ct);
        if (orders.Count == 0) return [];
        var ids = orders.Select(order => order.Id).ToList();
        // Historical lines intentionally do not join the live, possibly deleted menu item.
        var items = await db.OrderItems.AsNoTracking().Where(item => ids.Contains(item.OrderId)).OrderBy(item => item.Id).ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking().Where(payment => ids.Contains(payment.OrderId)).ToListAsync(ct);
        var history = await db.OrderStatusHistories.AsNoTracking().Where(entry => ids.Contains(entry.OrderId)).ToListAsync(ct);
        var itemGroups = items.ToLookup(item => item.OrderId);
        var paymentGroups = payments.ToLookup(payment => payment.OrderId);
        var historyGroups = history.ToLookup(entry => entry.OrderId);
        // SQLite cannot order DateTimeOffset natively; sort the materialized records.
        return orders.OrderByDescending(order => order.CreatedAt).ThenByDescending(order => order.Id)
            .Select(order => new StoredOrder(order, itemGroups[order.Id].ToList(), paymentGroups[order.Id].FirstOrDefault(),
                historyGroups[order.Id].OrderBy(entry => entry.ChangedAt).ThenBy(entry => entry.Id).ToList())).ToList();
    }

    public Task<Order?> FindAsync(int id, int? userId = null, CancellationToken ct = default) =>
        db.Orders.Include(order => order.OrderStatus).FirstOrDefaultAsync(order => order.Id == id && (userId == null || order.UserId == userId), ct);
    public void Add(Order order) => db.Orders.Add(order);
    public void AddItems(IEnumerable<OrderItem> items) => db.OrderItems.AddRange(items);
    public void AddPayment(Payment payment) => db.Payments.Add(payment);
    public void AddHistory(OrderStatusHistory history) => db.OrderStatusHistories.Add(history);
    public void AddNotification(Notification notification) => db.Notifications.Add(notification);
}
