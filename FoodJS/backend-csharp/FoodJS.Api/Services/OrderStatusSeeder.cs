using FoodJS.Api.Contracts;
using FoodJS.Api.Repositories;
using FoodJS.Api.Models;

namespace FoodJS.Api.Services;

// Seeds the OrderStatuses table with the default statuses if they are missing. This is called from Program.cs on startup.
public sealed class OrderStatusSeeder(IReferenceDataRepository referenceData, IUnitOfWorkRepository unitOfWork)
{
    private static readonly (string Name, string Description)[] Defaults =
    [
        (OrderStatusRules.Pending, "Order received and waiting for the kitchen."),
        (OrderStatusRules.Preparing, "The kitchen is preparing the order."),
        (OrderStatusRules.Ready, "Order is ready for pickup or delivery."),
        (OrderStatusRules.Delivered, "Order was delivered to the customer."),
        (OrderStatusRules.Cancelled, "Order was cancelled.")
    ];

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        var existing = await referenceData.ListStatusNamesAsync(ct);
        var missing = Defaults.Where(entry => !existing.Contains(entry.Name)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        foreach (var entry in missing)
            referenceData.AddStatus(new OrderStatus { Name = entry.Name, Description = entry.Description });
        await unitOfWork.SaveAsync(ct);
    }
}
