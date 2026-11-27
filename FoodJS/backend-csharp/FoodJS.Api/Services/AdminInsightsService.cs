using FoodJS.Api.Contracts;
using FoodJS.Api.Repositories;
namespace FoodJS.Api.Services;

public sealed class AdminInsightsService(IAdminInsightsRepository repository) : IAdminInsightsService
{
    public async Task<AdminInsightsResponse> Get(CancellationToken ct)
    {
        var orders = await repository.ListOrdersAsync(ct);
        var active = orders.Where(o => o.Status != OrderStatusRules.Cancelled).ToList();
        var today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date;
        return new(active.Count, active.Sum(o => o.TotalAmount), orders.Select(o => o.UserId).Distinct().Count(),
            active.Count(o => o.CreatedAt.ToOffset(TimeSpan.FromHours(8)).Date == today), DateTimeOffset.UtcNow);
    }
}
