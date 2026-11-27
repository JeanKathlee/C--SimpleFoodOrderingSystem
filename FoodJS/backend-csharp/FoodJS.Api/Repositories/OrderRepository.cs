using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;

namespace FoodJS.Api.Repositories;

public sealed class OrderRepository(FoodJsDbContext db) : IOrderRepository
{
    public async Task<IReadOnlyList<StoredOrder>> LoadAsync(int? userId = null, int? orderId = null, CancellationToken ct = default)
    {
        var query = db.Orders.AsNoTracking();
        if (userId is not null) query = query.Where(order => order.UserId == userId);
        if (orderId is not null) query = query.Where(order => order.Id == orderId);
        var orders = await query.ToListAsync(ct);
        await LoadHistoricalReferencesAsync(orders, ct);
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

    public async Task<Order?> FindAsync(int id, int? userId = null, CancellationToken ct = default)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id && (userId == null || o.UserId == userId), ct);
        if (order is not null)
            order.OrderStatus = await db.OrderStatuses.IgnoreQueryFilters().SingleAsync(s => s.Id == order.OrderStatusId, ct);
        return order;
    }
    private const string PageSql = """
        SELECT o.* FROM Orders o JOIN OrderStatuses s ON s.Id = o.OrderStatusId
        WHERE o.IsDeleted = 0 AND ({0} IS NULL OR o.UserId = {0}) AND ({1} IS NULL OR s.Name = {1})
          AND ({2} = '' OR instr(lower(o.CustomerName), {2}) > 0 OR instr(lower('O-' || o.Id), {2}) > 0)
          AND ({3} IS NULL OR julianday(o.CreatedAt) >= julianday({3}))
          AND ({4} IS NULL OR julianday(o.CreatedAt) < julianday({4}))
        """;
    private static object[] Arguments(OrderQuery query) => [query.UserId!, query.Status!, query.Search, query.FromUtc!, query.ToUtc!];

    public Task<int> CountAsync(OrderQuery query, CancellationToken ct) =>
        db.Orders.FromSql(FormattableStringFactory.Create(PageSql, Arguments(query))).CountAsync(ct);

    public async Task<IReadOnlyList<StoredOrder>> PageAsync(OrderQuery query, int limit, int offset, CancellationToken ct)
    {
        object[] args = [.. Arguments(query), limit, offset];
        var rows = await db.Orders.FromSql(FormattableStringFactory.Create(
            PageSql + " ORDER BY julianday(o.CreatedAt) DESC, o.Id DESC LIMIT {5} OFFSET {6}", args))
            .AsNoTracking().ToListAsync(ct);
        await LoadHistoricalReferencesAsync(rows, ct);
        var ids = rows.Select(o => o.Id).ToList();
        var lines = await db.OrderItems.AsNoTracking().Where(i => ids.Contains(i.OrderId)).ToListAsync(ct);
        var groups = lines.ToLookup(i => i.OrderId);
        return rows.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Select(o => new StoredOrder(o, groups[o.Id].ToList(), null, [])).ToList();
    }

    public Task<CheckoutSubmission?> FindSubmissionAsync(int userId, string key, CancellationToken ct) =>
        db.CheckoutSubmissions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId && s.Key == key, ct);
    // Historical references remain readable for an active order even if the
    // account/address/status was subsequently retired. Never join the live user.
    private async Task LoadHistoricalReferencesAsync(List<Order> orders, CancellationToken ct)
    {
        if (orders.Count == 0) return;
        var statusIds = orders.Select(o => o.OrderStatusId).Distinct().ToList();
        var addressIds = orders.Where(o => o.AddressId != null).Select(o => o.AddressId!.Value).Distinct().ToList();
        var statuses = await db.OrderStatuses.IgnoreQueryFilters().AsNoTracking()
            .Where(s => statusIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var addresses = await db.Addresses.IgnoreQueryFilters().AsNoTracking()
            .Where(a => addressIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        foreach (var order in orders)
        {
            order.OrderStatus = statuses[order.OrderStatusId];
            order.Address = order.AddressId is int id ? addresses.GetValueOrDefault(id) : null;
        }
    }

    public void AddSubmission(CheckoutSubmission submission) => db.CheckoutSubmissions.Add(submission);
    public void Add(Order order) => db.Orders.Add(order);
    public void AddItems(IEnumerable<OrderItem> items) => db.OrderItems.AddRange(items);
    public void AddPayment(Payment payment) => db.Payments.Add(payment);
    public void AddHistory(OrderStatusHistory history) => db.OrderStatusHistories.Add(history);
    public void AddNotification(Notification notification) => db.Notifications.Add(notification);
}
