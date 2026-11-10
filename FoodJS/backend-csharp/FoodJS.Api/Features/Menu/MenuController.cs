using FoodJS.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodJS.Api.Features.Menu;

[ApiController]
[Route("api/menu")]
public sealed class MenuController(IMenuService menuService) : ControllerBase
{
    // GET /api/menu is public: the customer menu page loads it without a token.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MenuItemResponse>>> GetAll(CancellationToken ct) =>
        Ok(await menuService.GetAllAsync(ct));

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
