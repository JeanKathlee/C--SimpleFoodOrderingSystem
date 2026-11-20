using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Features.Orders;

// Seeds the OrderStatuses table with the default statuses if they are missing. This is called from Program.cs on startup.
public static class OrderStatusSeeder
{
    private static readonly (string Name, string Description)[] Defaults =
    [
        (OrderStatusRules.Pending, "Order received and waiting for the kitchen."),
        (OrderStatusRules.Preparing, "The kitchen is preparing the order."),
        (OrderStatusRules.Ready, "Order is ready for pickup or delivery."),
        (OrderStatusRules.Delivered, "Order was delivered to the customer."),
        (OrderStatusRules.Cancelled, "Order was cancelled.")
    ];

    public static async Task EnsureSeededAsync(FoodJsDbContext db, CancellationToken ct = default)
    {
        var existing = await db.OrderStatuses.Select(status => status.Name).ToListAsync(ct);
        var missing = Defaults.Where(entry => !existing.Contains(entry.Name)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        db.OrderStatuses.AddRange(missing.Select(entry => new OrderStatus
        {
            Name = entry.Name,
            Description = entry.Description
        }));
        await db.SaveChangesAsync(ct);
    }
}
