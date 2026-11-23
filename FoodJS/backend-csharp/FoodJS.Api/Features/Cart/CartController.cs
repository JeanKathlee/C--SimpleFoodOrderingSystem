using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Features.Cart;

public sealed record CartItemResponse(int UserId, string MenuItemId, string Name, decimal Price, int Quantity, string? SelectedChoice, string? Image);

// price from the client is ignored: the cart shows the database price, the same price OrderService charges
public sealed record AddCartItemRequest(string? MenuItemId, int? Quantity, string? SelectedChoice);

public sealed record UpdateCartItemRequest(int? Quantity);

// same urls as the old node api (backend/routes/cart.js). each logged-in user has one cart
[ApiController]
[Route("api/cart")]
[Authorize]
public sealed class CartController(FoodJsDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<CartItemResponse>> GetMine(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var items = await db.CartItems.AsNoTracking()
            .Include(i => i.MenuItem)
            .Where(i => i.ShoppingCart.UserId == userId)
            .OrderBy(i => i.Id)
            .ToListAsync(ct);
        return items.Select(i => new CartItemResponse(
            userId, PublicId.Menu(i.MenuItemId), i.MenuItem.Name, i.MenuItem.Price, i.Quantity, i.SelectedChoice, i.MenuItem.Image)).ToList();
    }

    [HttpPost("items")]
    public async Task<IActionResult> Add(AddCartItemRequest request, CancellationToken ct)
    {
        if (request.Quantity is null || !PublicId.TryParseMenu(request.MenuItemId, out var menuId))
        {
            return BadRequest(new { message = "menuItemId and quantity are required." });
        }
        if (request.Quantity <= 0)
        {
            return BadRequest(new { message = "Quantity must be greater than 0." });
        }
        if (!await db.MenuItems.AnyAsync(m => m.Id == menuId && m.IsAvailable, ct))
        {
            return NotFound(new { message = "Menu item not found or unavailable." });
        }

        var userId = User.RequireUserId();
        var cart = await db.ShoppingCarts.FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? db.ShoppingCarts.Add(new ShoppingCart { UserId = userId }).Entity;
        var existing = cart.Id == 0
            ? null
            : await db.CartItems.FirstOrDefaultAsync(i => i.ShoppingCartId == cart.Id && i.MenuItemId == menuId, ct);

        // same as node: adding an item already in the cart adds to its quantity
        if (existing is null)
        {
            db.CartItems.Add(new CartItem { ShoppingCart = cart, MenuItemId = menuId, Quantity = request.Quantity.Value, SelectedChoice = request.SelectedChoice });
        }
        else
        {
            existing.Quantity += request.Quantity.Value;
            existing.SelectedChoice = request.SelectedChoice ?? existing.SelectedChoice;
        }
        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return StatusCode(StatusCodes.Status201Created, new { message = "Item added to cart." });
    }

    [HttpPatch("items/{menuId}")]
    public async Task<IActionResult> Update(string menuId, UpdateCartItemRequest request, CancellationToken ct)
    {
        if (request.Quantity is null)
        {
            return BadRequest(new { message = "Quantity is required." });
        }
        if (request.Quantity <= 0)
        {
            return BadRequest(new { message = "Quantity must be greater than 0." });
        }

        var item = await FindMineAsync(menuId, ct);
        if (item is null)
        {
            return NotFoundResult();
        }

        item.Quantity = request.Quantity.Value;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Cart item updated." });
    }

    [HttpDelete("items/{menuId}")]
    public async Task<IActionResult> Remove(string menuId, CancellationToken ct)
    {
        var item = await FindMineAsync(menuId, ct);
        if (item is null)
        {
            return NotFoundResult();
        }

        db.CartItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Item removed from cart." });
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        await db.CartItems.Where(i => i.ShoppingCart.UserId == userId).ExecuteDeleteAsync(ct);
        return Ok(new { message = "Cart cleared." });
    }

    private async Task<CartItem?> FindMineAsync(string menuId, CancellationToken ct)
    {
        var userId = User.RequireUserId();
        return PublicId.TryParseMenu(menuId, out var id)
            ? await db.CartItems.FirstOrDefaultAsync(i => i.ShoppingCart.UserId == userId && i.MenuItemId == id, ct)
            : null;
    }

    private NotFoundObjectResult NotFoundResult() => NotFound(new { message = "Item not found in cart." });
}
