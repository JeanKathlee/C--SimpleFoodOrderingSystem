using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Features.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Features.AdminInsights;

public sealed record AdminInsightsResponse(int TotalOrders, decimal TotalRevenue, int TotalCustomers, int TodayOrders, DateTimeOffset LastUpdated);

// same numbers as the old node api (backend/routes/adminInsights.js). cancelled orders don't count.
// node left this public; here it is admin only because it shows revenue
[ApiController]
[Route("api/admin-insights")]
[Authorize(Roles = AuthRoles.Admin)]
public sealed class AdminInsightsController(FoodJsDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<AdminInsightsResponse> Get(CancellationToken ct)
    {
        // shortcut: loads every order into memory (SQLite can't SUM decimals), move to SQL if orders grow into the thousands
        var orders = await db.Orders.AsNoTracking()
            .Select(o => new { o.UserId, o.TotalAmount, o.CreatedAt, Status = o.OrderStatus.Name })
            .ToListAsync(ct);
        var active = orders.Where(o => o.Status != OrderStatusRules.Cancelled).ToList();
        var today = DateTimeOffset.Now.Date;

        return new AdminInsightsResponse(
            active.Count,
            active.Sum(o => o.TotalAmount),
            orders.Select(o => o.UserId).Distinct().Count(),
            active.Count(o => o.CreatedAt.ToLocalTime().Date == today),
            DateTimeOffset.UtcNow);
    }
}
