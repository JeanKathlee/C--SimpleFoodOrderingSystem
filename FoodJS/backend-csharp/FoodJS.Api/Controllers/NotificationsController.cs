using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using FoodJS.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FoodJS.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationService service) : ControllerBase
{
    [HttpGet]
    public Task<List<NotificationResponse>> GetMine(CancellationToken ct) => service.GetMine(User.RequireUserId(), ct);

    [HttpPatch("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
    {
        var notification = await service.MarkRead(id, User.RequireUserId(), ct);
        return notification is null ? NotFound(new { message = "Notification not found." }) : Ok(notification);
    }
}
