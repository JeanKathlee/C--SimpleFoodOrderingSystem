using FoodJS.Api.Contracts;

namespace FoodJS.Api.Services;

public interface IAdminInsightsService
{
    Task<AdminInsightsResponse> Get(CancellationToken ct);
}
