using FoodJS.Api.Contracts;
using FoodJS.Api.Common;
using FoodJS.Api.Repositories;
namespace FoodJS.Api.Services;

public sealed class NotificationService(INotificationRepository repository, IUnitOfWorkRepository unit) : INotificationService
{
    public async Task<List<NotificationResponse>> GetMine(int userId, CancellationToken ct) =>
        (await repository.ListAsync(userId, ct)).OrderByDescending(n => n.CreatedAt).Select(ToResponse).ToList();

    public async Task<NotificationResponse?> MarkRead(int id, int userId, CancellationToken ct)
    {
        var notification = await repository.FindOwnedAsync(id, userId, ct);
        if (notification is null) return null;
        notification.IsRead = true;
        await unit.SaveAsync(ct);
        return ToResponse(notification);
    }

    private static NotificationResponse ToResponse(Models.Notification n) =>
        new(n.Id, n.UserId, n.OrderId is int orderId ? PublicId.Order(orderId) : null, n.Title, n.Message, n.Type, "in-app", n.IsRead, n.CreatedAt);
}
