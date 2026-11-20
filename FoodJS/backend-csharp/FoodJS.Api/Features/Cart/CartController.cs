using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Features.Cart;

[ApiController]
[Route("api/cart")]
[Authorize]
public sealed class CartController(FoodJsDbContext db) : ControllerBase
{
    private const int MaximumQuantity = 99;

    [HttpGet]
    public async Task<ActionResult<List<CartItemResponse>>> GetMine(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var items = await db.CartItems
            .AsNoTracking()
            .Include(item => item.MenuItem)
            .Where(item => item.ShoppingCart.UserId == userId)
            .OrderBy(item => item.Id)
            .ToListAsync(ct);

        return Ok(items.Select(item => ToResponse(item, userId)).ToList());
    }

    [HttpPost("items")]
    public async Task<IActionResult> Add(AddCartItemRequest request, CancellationToken ct)
    {
        var userId = User.RequireUserId();

        if (!PublicId.TryParseMenu(request.MenuItemId, out var menuItemId))
        {
            return BadRequest(new { message = "A valid menuItemId is required." });
        }

        if (!IsValidQuantity(request.Quantity))
        {
            return InvalidQuantityResult();
        }

        if (!CartOptionPricing.TryResolve(request.SelectedChoice, out var selectedChoice, out var addOn))
        {
            return BadRequest(new { message = "Invalid selectedChoice." });
        }

        var menuItem = await db.MenuItems
            .FirstOrDefaultAsync(item => item.Id == menuItemId && item.IsAvailable, ct);
        if (menuItem is null)
        {
            return NotFound(new { message = "Menu item not found or unavailable." });
        }

        var cart = await db.ShoppingCarts.FirstOrDefaultAsync(entry => entry.UserId == userId, ct);
        if (cart is null)
        {
            cart = new ShoppingCart { UserId = userId };
            db.ShoppingCarts.Add(cart);
        }

        CartItem? existing = null;
        if (cart.Id != 0)
        {
            existing = await db.CartItems.FirstOrDefaultAsync(
                item => item.ShoppingCartId == cart.Id
                    && item.MenuItemId == menuItemId
                    && item.SelectedChoice == selectedChoice,
                ct);
        }

        var unitPrice = decimal.Round(menuItem.Price + addOn, 2);
        if (existing is null)
        {
            db.CartItems.Add(new CartItem
            {
                ShoppingCart = cart,
                MenuItemId = menuItemId,
                Quantity = request.Quantity!.Value,
                UnitPrice = unitPrice,
                SelectedChoice = selectedChoice
            });
        }
        else
        {
            var combinedQuantity = existing.Quantity + request.Quantity!.Value;
            if (combinedQuantity > MaximumQuantity)
            {
                return InvalidQuantityResult();
            }

            existing.Quantity = combinedQuantity;
            existing.UnitPrice = unitPrice;
        }

        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return StatusCode(StatusCodes.Status201Created, new { message = "Item added to cart." });
    }

    [HttpPatch("items/{cartItemId:int}")]
    public async Task<IActionResult> Update(
        int cartItemId,
        UpdateCartItemRequest request,
        CancellationToken ct)
    {
        var userId = User.RequireUserId();
        if (!IsValidQuantity(request.Quantity))
        {
            return InvalidQuantityResult();
        }

        var item = await FindMineAsync(cartItemId, userId, ct);
        if (item is null)
        {
            return ItemNotFoundResult();
        }

        item.Quantity = request.Quantity!.Value;
        item.ShoppingCart.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Cart item updated." });
    }

    [HttpDelete("items/{cartItemId:int}")]
    public async Task<IActionResult> Remove(int cartItemId, CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var item = await FindMineAsync(cartItemId, userId, ct);
        if (item is null)
        {
            return ItemNotFoundResult();
        }

        item.ShoppingCart.UpdatedAt = DateTimeOffset.UtcNow;
        db.CartItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Item removed from cart." });
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var cart = await db.ShoppingCarts.FirstOrDefaultAsync(entry => entry.UserId == userId, ct);
        if (cart is not null)
        {
            var items = await db.CartItems
                .Where(item => item.ShoppingCartId == cart.Id)
                .ToListAsync(ct);
            db.CartItems.RemoveRange(items);
            cart.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return Ok(new { message = "Cart cleared." });
    }

    private async Task<CartItem?> FindMineAsync(int cartItemId, int userId, CancellationToken ct) =>
        await db.CartItems
            .Include(item => item.ShoppingCart)
            .FirstOrDefaultAsync(
                item => item.Id == cartItemId && item.ShoppingCart.UserId == userId,
                ct);

    private static bool IsValidQuantity(int? quantity) =>
        quantity is >= 1 and <= MaximumQuantity;

    private static CartItemResponse ToResponse(CartItem item, int userId) => new(
        item.Id,
        userId,
        PublicId.Menu(item.MenuItemId),
        item.MenuItem.Name,
        item.UnitPrice,
        item.Quantity,
        item.SelectedChoice,
        item.MenuItem.Image);

    private BadRequestObjectResult InvalidQuantityResult() =>
        BadRequest(new { message = $"Quantity must be between 1 and {MaximumQuantity}." });

    private NotFoundObjectResult ItemNotFoundResult() =>
        NotFound(new { message = "Item not found in cart." });
}
