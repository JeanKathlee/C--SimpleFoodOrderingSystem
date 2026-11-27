using FoodJS.Api.Data;
using Microsoft.EntityFrameworkCore;
namespace FoodJS.Api.Repositories;

public sealed class AdminInsightsRepository(FoodJsDbContext db) : IAdminInsightsRepository
{
    public Task<List<OrderInsight>> ListOrdersAsync(CancellationToken ct) =>
        (from order in db.Orders.AsNoTracking()
         join status in db.OrderStatuses.IgnoreQueryFilters() on order.OrderStatusId equals status.Id
         select new OrderInsight(order.UserId, order.TotalAmount, order.CreatedAt, status.Name)).ToListAsync(ct);
}
