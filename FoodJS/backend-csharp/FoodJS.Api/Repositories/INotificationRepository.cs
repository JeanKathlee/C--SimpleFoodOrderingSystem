using FoodJS.Api.Models;
namespace FoodJS.Api.Repositories;
public interface INotificationRepository
{
    Task<List<Notification>> ListAsync(int userId, CancellationToken ct);
    Task<Notification?> FindOwnedAsync(int id, int userId, CancellationToken ct);
}
