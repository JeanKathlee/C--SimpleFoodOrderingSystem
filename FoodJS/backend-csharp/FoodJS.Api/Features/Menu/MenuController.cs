using FoodJS.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodJS.Api.Features.Menu;

// reading the menu (list, search, filter, details) uses MenuService.
// changing the menu (add, edit, availability, delete) uses IMenuService (EfMenuService)
[ApiController]
[Route("api/menu")]
public sealed class MenuController(IMenuService menuService, MenuService menuDb) : ControllerBase
{
    // anyone can see the menu. if an admin is logged in, hidden items are shown too
    // search and filter: /api/menu?search=fries&category=Sides
    [HttpGet]
    public async Task<ActionResult<List<MenuItemResponse>>> GetAll(string? search, string? category) =>
        Ok(await menuDb.GetAllAsync(User.IsInRole(AuthRoles.Admin), search, category));

    [HttpGet("{id}")]
    public async Task<ActionResult<MenuItemResponse>> Get(string id)
    {
        if (!PublicId.TryParseMenu(id, out var menuId))
        {
            return NotFoundResult();
        }

        var item = await menuDb.GetAsync(menuId, User.IsInRole(AuthRoles.Admin));
        return item is null ? NotFoundResult() : Ok(item);
    }

    [HttpPost]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<MenuItemResponse>> Create(SaveMenuItemRequest request, CancellationToken ct)
    {
        var item = await menuService.CreateAsync(request, User.GetUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, item);
    }

    // The admin dashboard also toggles availability through this full update (isAvailable in the body).
    [HttpPut("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<MenuItemResponse>> Update(string id, SaveMenuItemRequest request, CancellationToken ct)
    {
        if (!PublicId.TryParseMenu(id, out var menuId))
        {
            return NotFoundResult();
        }

        var item = await menuService.UpdateAsync(menuId, request, ct);
        return item is null ? NotFoundResult() : Ok(item);
    }

    // Additive endpoint for the "Change Availability" task: flip availability without
    // resending the whole item. Existing frontend calls are unaffected.
    [HttpPatch("{id}/availability")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<MenuItemResponse>> SetAvailability(
        string id, SetAvailabilityRequest request, CancellationToken ct)
    {
        if (request.IsAvailable is null)
        {
            return BadRequest(new { message = "isAvailable is required." });
        }

        if (!PublicId.TryParseMenu(id, out var menuId))
        {
            return NotFoundResult();
        }

        var item = await menuService.SetAvailabilityAsync(menuId, request.IsAvailable.Value, ct);
        return item is null ? NotFoundResult() : Ok(item);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (!PublicId.TryParseMenu(id, out var menuId) || !await menuService.DeleteAsync(menuId, ct))
        {
            return NotFoundResult();
        }

        return Ok(new { message = "Menu item deleted successfully." });
    }

    private NotFoundObjectResult NotFoundResult() => NotFound(new { message = "Menu item not found." });
}
