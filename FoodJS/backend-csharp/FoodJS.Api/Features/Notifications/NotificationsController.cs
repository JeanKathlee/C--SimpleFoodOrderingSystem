using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Features.Notifications;

// status is the order status name OrderService saves in Type. only in-app notifications are stored
public sealed record NotificationResponse(
    int Id, int UserId, string? OrderId, string Title, string Message, string Status, string Channel, bool Read, DateTimeOffset CreatedAt);

// same urls as the old node api (backend/routes/notifications.js). the rows are made by OrderService
[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(FoodJsDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<NotificationResponse>> GetMine(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var rows = await db.Notifications.AsNoTracking().Where(n => n.UserId == userId).ToListAsync(ct);
        // sorted in memory because SQLite cannot ORDER BY a DateTimeOffset column
        return rows.OrderByDescending(n => n.CreatedAt).Select(ToResponse).ToList();
    }

    [HttpPatch("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
        if (notification is null)
        {
            return NotFound(new { message = "Notification not found." });
        }

        notification.IsRead = true;
        await db.SaveChangesAsync(ct);
        return Ok(ToResponse(notification));
    }

    private static NotificationResponse ToResponse(Notification n) => new(
        n.Id, n.UserId, n.OrderId is int orderId ? PublicId.Order(orderId) : null,
        n.Title, n.Message, n.Type, "in-app", n.IsRead, n.CreatedAt);
}
