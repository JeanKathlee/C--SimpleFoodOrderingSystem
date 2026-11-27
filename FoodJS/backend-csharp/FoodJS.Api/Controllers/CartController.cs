using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using FoodJS.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FoodJS.Api.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize]
public sealed class CartController(ICartService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CartItemResponse>>> GetMine(CancellationToken ct) =>
        Ok(await service.GetMine(User.RequireUserId(), ct));

    [HttpPost("items")]
    public async Task<IActionResult> Add(AddCartItemRequest request, CancellationToken ct)
    {
        await service.Add(User.RequireUserId(), request, ct);
        return StatusCode(201, new { message = "Item added to cart." });
    }

    [HttpPatch("items/{cartItemId:int}")]
    public async Task<IActionResult> Update(int cartItemId, UpdateCartItemRequest request, CancellationToken ct)
    {
        await service.Update(User.RequireUserId(), cartItemId, request, ct);
        return Ok(new { message = "Cart item updated." });
    }

    [HttpDelete("items/{cartItemId:int}")]
    public async Task<IActionResult> Remove(int cartItemId, CancellationToken ct)
    {
        await service.Remove(User.RequireUserId(), cartItemId, ct);
        return Ok(new { message = "Item removed from cart." });
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await service.Clear(User.RequireUserId(), ct);
        return Ok(new { message = "Cart cleared." });
    }
}
