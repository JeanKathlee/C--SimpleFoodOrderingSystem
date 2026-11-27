using FoodJS.Api.Contracts;
namespace FoodJS.Api.Services;
public sealed class HealthService(IHostEnvironment environment)
{
    public HealthResponse Get() => new("ok", "FoodJS C# API", environment.EnvironmentName);
}
