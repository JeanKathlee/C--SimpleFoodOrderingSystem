using FoodJS.Api.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodJS.Api.Features.Menu;

[ApiController]
[Route("api/menu")]
public sealed class MenuController(IMenuService menuService) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyCollection<MenuItemResponse>> GetAll() =>
        Ok(menuService.GetAll());

    [HttpPost]
    [Authorize(Roles = AuthService.AdminRole)]
    public ActionResult<MenuItemResponse> Create(SaveMenuItemRequest request)
    {
        var item = menuService.Create(request);
        return StatusCode(StatusCodes.Status201Created, item);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = AuthService.AdminRole)]
    public ActionResult<MenuItemResponse> Update(string id, SaveMenuItemRequest request)
    {
        var item = menuService.Update(id, request);
        return item is null
            ? NotFound(new { message = "Menu item not found." })
            : Ok(item);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = AuthService.AdminRole)]
    public IActionResult Delete(string id) => menuService.Delete(id)
        ? Ok(new { message = "Menu item deleted successfully." })
        : NotFound(new { message = "Menu item not found." });
}
