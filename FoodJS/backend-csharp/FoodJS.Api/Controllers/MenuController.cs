using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using FoodJS.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FoodJS.Api.Controllers;

[ApiController]
[Route("api/menu")]
public sealed class MenuController(IMenuService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MenuItemResponse>>> GetAll(string? search, string? category) =>
        Ok(await service.GetAllAsync(User.IsInRole(AuthRoles.Admin), search, category));

    [HttpGet("paged")]
    public async Task<ActionResult<MenuPageResponse>> GetPage([FromQuery] MenuPageRequest request, CancellationToken ct) =>
        Ok(await service.GetPageAsync(request, User.IsInRole(AuthRoles.Admin), ct));

    [HttpGet("{id}")]
    public async Task<ActionResult<MenuItemResponse>> Get(string id)
    {
        var item = await service.GetByPublicIdAsync(id, User.IsInRole(AuthRoles.Admin));
        return item is null ? NotFoundResult() : Ok(item);
    }

    [HttpPost]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<MenuItemResponse>> Create(SaveMenuItemRequest request, CancellationToken ct) =>
        StatusCode(201, await service.CreateAsync(request, User.GetUserId(), ct));

    [HttpPut("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<MenuItemResponse>> Update(string id, SaveMenuItemRequest request, CancellationToken ct)
    {
        var item = await service.UpdateByPublicIdAsync(id, request, ct);
        return item is null ? NotFoundResult() : Ok(item);
    }

    [HttpPatch("{id}/availability")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<MenuItemResponse>> SetAvailability(string id, SetAvailabilityRequest request, CancellationToken ct)
    {
        var item = await service.SetAvailabilityByPublicIdAsync(id, request.IsAvailable, ct);
        return item is null ? NotFoundResult() : Ok(item);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct) =>
        await service.DeleteByPublicIdAsync(id, ct) ? Ok(new { message = "Menu item deleted successfully." }) : NotFoundResult();

    private NotFoundObjectResult NotFoundResult() => NotFound(new { message = "Menu item not found." });
}
