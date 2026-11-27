using FoodJS.Api.Contracts;

namespace FoodJS.Api.Services;

public interface INotificationService
{
    Task<List<NotificationResponse>> GetMine(int userId, CancellationToken ct);
    Task<NotificationResponse?> MarkRead(int id, int userId, CancellationToken ct);
}
