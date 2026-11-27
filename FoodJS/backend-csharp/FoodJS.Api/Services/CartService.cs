using FoodJS.Api.Contracts;
using FoodJS.Api.Common;
using FoodJS.Api.Repositories;
using FoodJS.Api.Models;

namespace FoodJS.Api.Services;

public sealed class CartService(ICartRepository repository, IUnitOfWorkRepository unitOfWork) : ICartService
{
    private const int MaximumQuantity = 99;

    public async Task<List<CartItemResponse>> GetMine(int userId, CancellationToken ct)
    {
        var items = await repository.ListAsync(userId, ct);

        return items.Select(item => ToResponse(item, userId)).ToList();
    }

    public async Task Add(int userId, AddCartItemRequest request, CancellationToken ct)
    {
        if (!PublicId.TryParseMenu(request.MenuItemId, out var menuItemId))
        {
            throw new ApiException(400, "A valid menuItemId is required.");
        }

        if (!IsValidQuantity(request.Quantity))
        {
            throw QuantityError();
        }

        var menuItem = await repository.FindAvailableMenuAsync(menuItemId, ct);
        if (menuItem is null)
        {
            throw new ApiException(404, "Menu item not found or unavailable.");
        }

        var option = MenuDrinkOptionRules.Resolve(menuItem, request.SelectedChoice);
        var selectedChoice = option?.Label;
        var addOn = option?.AddOn ?? 0m;

        var cart = await repository.FindCartAsync(userId, ct);
        if (cart is null)
        {
            cart = new ShoppingCart { UserId = userId };
            repository.AddCart(cart);
        }

        CartItem? existing = null;
        if (cart.Id != 0)
        {
            existing = await repository.FindMatchingAsync(cart.Id, menuItemId, selectedChoice, ct);
        }

        var unitPrice = decimal.Round(menuItem.Price + addOn, 2);
        if (existing is null)
        {
            repository.AddItem(new CartItem
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
                throw QuantityError();
            }

            existing.Quantity = combinedQuantity;
            existing.UnitPrice = unitPrice;
        }

        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveAsync(ct);
    }

    public async Task Update(
        int userId, int cartItemId,
        UpdateCartItemRequest request,
        CancellationToken ct)
    {
        if (!IsValidQuantity(request.Quantity))
        {
            throw QuantityError();
        }

        var item = await repository.FindOwnedAsync(cartItemId, userId, ct);
        if (item is null)
        {
            throw new ApiException(404, "Item not found in cart.");
        }

        item.Quantity = request.Quantity!.Value;
        item.ShoppingCart.UpdatedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveAsync(ct);

    }

    public async Task Remove(int userId, int cartItemId, CancellationToken ct)
    {
        var item = await repository.FindOwnedAsync(cartItemId, userId, ct);
        if (item is null)
        {
            throw new ApiException(404, "Item not found in cart.");
        }

        item.IsDeleted = true;
        await unitOfWork.SaveAsync(ct);

    }

    public async Task Clear(int userId, CancellationToken ct)
    {
        var cart = await repository.FindCartAsync(userId, ct);
        if (cart is not null)
        {
            var items = await repository.ListForCartAsync(cart.Id, ct);
            foreach (var item in items) item.IsDeleted = true;
            await unitOfWork.SaveAsync(ct);
        }
    }

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

    private static ApiException QuantityError() =>
        new(400, $"Quantity must be between 1 and {MaximumQuantity}.");
}
