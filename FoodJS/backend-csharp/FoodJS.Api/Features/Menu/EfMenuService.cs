using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Features.Menu;

public sealed class EfMenuService(FoodJsDbContext db) : IMenuService
{
    private const int MaxNameLength = 120;
    private const int MaxDescriptionLength = 2_000;

    public async Task<MenuPageResponse> GetPageAsync(MenuPageRequest request, CancellationToken ct = default)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 50)
            throw new ApiException(StatusCodes.Status400BadRequest, "Page must be positive and pageSize must be between 1 and 50.");
        var query = db.MenuItems.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.CategoryId))
        {
            if (!PublicId.TryParseCategory(request.CategoryId, out var categoryId))
                throw new ApiException(StatusCodes.Status400BadRequest, "Invalid categoryId.");
            query = query.Where(item => item.CategoryId == categoryId);
        }
        if (request.IsAvailable is { } available)
            query = query.Where(item => item.IsAvailable == available);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(item => item.Name.ToLower().Contains(search) || item.Description.ToLower().Contains(search));
        }
        var count = await query.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)request.PageSize));
        var page = Math.Min(request.Page, totalPages);
        var items = await query.Include(item => item.Category)
            .OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Skip((page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new(items.Select(ToResponse).ToList(), count, page, request.PageSize, totalPages);
    }

    public async Task<IReadOnlyList<MenuItemResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await db.MenuItems
            .AsNoTracking()
            .Include(item => item.Category)
            .OrderBy(item => item.Id)
            .ToListAsync(ct);

        return items.Select(ToResponse).ToList();
    }

    public async Task<MenuItemResponse> CreateAsync(
        SaveMenuItemRequest request, int? adminId, CancellationToken ct = default)
    {
        var input = Validate(request);
        var category = await FindCategoryAsync(input.CategoryId, ct);
        var now = DateTimeOffset.UtcNow;

        var item = new MenuItem
        {
            Name = input.Name,
            Description = request.Description ?? string.Empty,
            Price = input.Price,
            PrepTime = input.PrepTime,
            IsAvailable = request.IsAvailable != false,
            Image = NormalizeImage(request.Image),
            CategoryId = category.Id,
            Category = category,
            CreatedByAdminId = await ResolveAdminIdAsync(adminId, ct),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.MenuItems.Add(item);
        await db.SaveChangesAsync(ct);
        return ToResponse(item);
    }

    public async Task<MenuItemResponse?> UpdateAsync(
        int id, SaveMenuItemRequest request, CancellationToken ct = default)
    {
        var item = await db.MenuItems
            .Include(entry => entry.Category)
            .FirstOrDefaultAsync(entry => entry.Id == id, ct);

        if (item is null)
        {
            return null;
        }

        var input = Validate(request);
        var category = await FindCategoryAsync(input.CategoryId, ct);

        item.Name = input.Name;
        item.Description = request.Description ?? item.Description;
        item.Price = input.Price;
        item.PrepTime = input.PrepTime;
        item.IsAvailable = request.IsAvailable ?? item.IsAvailable;
        item.CategoryId = category.Id;
        item.Category = category;
        if (request.Image is not null)
        {
            item.Image = NormalizeImage(request.Image);
        }

        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return ToResponse(item);
    }

    public async Task<MenuItemResponse?> SetAvailabilityAsync(
        int id, bool isAvailable, CancellationToken ct = default)
    {
        var item = await db.MenuItems
            .Include(entry => entry.Category)
            .FirstOrDefaultAsync(entry => entry.Id == id, ct);

        if (item is null)
        {
            return null;
        }

        item.IsAvailable = isAvailable;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return ToResponse(item);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var item = await db.MenuItems.FirstOrDefaultAsync(entry => entry.Id == id, ct);
        if (item is null)
        {
            return false;
        }

        // OrderItems.MenuItemId is ON DELETE CASCADE in the current schema, so deleting a
        // menu item that was ever ordered would silently erase lines from past orders and
        // break their totals. The Node API kept order snapshots, so we refuse instead.
        var isOrdered = await db.OrderItems.AnyAsync(line => line.MenuItemId == id, ct);
        if (isOrdered)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "This item appears in existing orders and cannot be deleted. Mark it as unavailable instead.");
        }

        // Cart lines for the item are removed by the Cascade FK on CartItems.
        db.MenuItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private sealed record ValidatedInput(string Name, string CategoryId, decimal Price, int PrepTime);

    private static ValidatedInput Validate(SaveMenuItemRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)
            || string.IsNullOrWhiteSpace(request.CategoryId)
            || request.Price is null
            || request.PrepTime is null)
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                "Name, categoryId, price, and prepTime are required.");
        }

        if (request.Price <= 0 || request.PrepTime <= 0)
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                "Price and prepTime must be greater than 0.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                $"Name must be {MaxNameLength} characters or fewer.");
        }

        if (request.Description is { Length: > MaxDescriptionLength })
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                $"Description must be {MaxDescriptionLength} characters or fewer.");
        }

        return new ValidatedInput(name, request.CategoryId.Trim(), request.Price.Value, request.PrepTime.Value);
    }

    private async Task<Category> FindCategoryAsync(string publicCategoryId, CancellationToken ct)
    {
        if (PublicId.TryParseCategory(publicCategoryId, out var categoryId))
        {
            var category = await db.Categories.FirstOrDefaultAsync(entry => entry.Id == categoryId, ct);
            if (category is not null)
            {
                return category;
            }
        }

        throw new ApiException(StatusCodes.Status400BadRequest, "Invalid categoryId.");
    }

    // CreatedByAdminId is a foreign key; only store it when that user really exists
    // (e.g. a demo token may carry an id that is not in the Users table).
    private async Task<int?> ResolveAdminIdAsync(int? adminId, CancellationToken ct)
    {
        if (adminId is null)
        {
            return null;
        }

        var exists = await db.Users.AnyAsync(user => user.Id == adminId.Value, ct);
        return exists ? adminId : null;
    }

    private static string? NormalizeImage(string? image) =>
        string.IsNullOrWhiteSpace(image) ? null : image;

    private static MenuItemResponse ToResponse(MenuItem item) => new(
        PublicId.Menu(item.Id),
        item.Name,
        item.Description,
        PublicId.Category(item.CategoryId),
        item.Category.Name,
        item.Price,
        item.PrepTime,
        item.IsAvailable,
        item.SoldCount,
        item.Image,
        item.CreatedAt,
        item.UpdatedAt);
}
