namespace FoodJS.Api.Repositories;
public sealed record OrderInsight(int UserId, decimal TotalAmount, DateTimeOffset CreatedAt, string Status);
public interface IAdminInsightsRepository
{
    Task<List<OrderInsight>> ListOrdersAsync(CancellationToken ct);
}
