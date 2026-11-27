using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;
namespace FoodJS.Api.Repositories;
public sealed class NotificationRepository(FoodJsDbContext db) : INotificationRepository
{
    public Task<List<Notification>> ListAsync(int userId, CancellationToken ct) => db.Notifications.AsNoTracking().Where(n => n.UserId == userId).ToListAsync(ct);
    public Task<Notification?> FindOwnedAsync(int id, int userId, CancellationToken ct) => db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
}
