using FoodJS.Api.Contracts;
using FoodJS.Api.Common;
using FoodJS.Api.Repositories;
using System.Text.Json;
using FoodJS.Api.Models;

namespace FoodJS.Api.Services;

public sealed class MenuService(IMenuRepository menuRepository, IReferenceDataRepository referenceData, IUnitOfWorkRepository unitOfWork) : IMenuService
{
    public async Task<MenuPageResponse> GetPageAsync(MenuPageRequest request, bool showHidden, CancellationToken ct = default)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 50)
            throw new ApiException(400, "Page must be positive and pageSize must be between 1 and 50.");
        int? categoryId = null;
        if (!string.IsNullOrWhiteSpace(request.CategoryId))
        {
            if (!PublicId.TryParseCategory(request.CategoryId, out var parsed))
                throw new ApiException(400, "Invalid categoryId.");
            categoryId = parsed;
        }
        // Public/customer reads cannot expose unavailable items through pagination.
        var filter = new MenuQuery(categoryId, showHidden ? request.IsAvailable : true, NormalizeFilter(request.Search));
        var count = await menuRepository.CountAsync(filter, ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)request.PageSize));
        var page = Math.Min(request.Page, totalPages);
        var rows = await menuRepository.PageAsync(filter, request.PageSize, (page - 1) * request.PageSize, ct);
        return new(rows.Select(ToResponse).ToList(), count, page, request.PageSize, totalPages);
    }

    public Task<MenuItemResponse?> GetByPublicIdAsync(string id, bool showHidden, CancellationToken ct = default) =>
        PublicId.TryParseMenu(id, out var key) ? GetAsync(key, showHidden, ct) : Task.FromResult<MenuItemResponse?>(null);
    public Task<MenuItemResponse?> UpdateByPublicIdAsync(string id, SaveMenuItemRequest request, CancellationToken ct = default) =>
        PublicId.TryParseMenu(id, out var key) ? UpdateAsync(key, request, ct) : Task.FromResult<MenuItemResponse?>(null);
    public Task<MenuItemResponse?> SetAvailabilityByPublicIdAsync(string id, bool? available, CancellationToken ct = default) =>
        PublicId.TryParseMenu(id, out var key) ? SetAvailabilityAsync(key, available, ct) : Task.FromResult<MenuItemResponse?>(null);
    public Task<bool> DeleteByPublicIdAsync(string id, CancellationToken ct = default) =>
        PublicId.TryParseMenu(id, out var key) ? DeleteAsync(key, ct) : Task.FromResult(false);

    private const int MaxNameLength = 120;
    private const int MaxDescriptionLength = 2_000;

    public async Task<IReadOnlyList<MenuItemResponse>> GetAllAsync(bool showHidden, string? search = null, string? category = null, CancellationToken ct = default)
    {
        var items = await menuRepository.ListAsync(showHidden, NormalizeFilter(search), NormalizeFilter(category), ct);
        return items.Select(ToResponse).ToList();
    }

    public async Task<MenuItemResponse?> GetAsync(int id, bool showHidden, CancellationToken ct = default)
    {
        var item = await menuRepository.FindAsync(id, ct);
        return item is null || (!showHidden && !item.IsAvailable) ? null : ToResponse(item);
    }

    private static string? NormalizeFilter(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

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

        menuRepository.Add(item);
        await unitOfWork.SaveAsync(ct);
        return ToResponse(item);
    }

    public async Task<MenuItemResponse?> UpdateAsync(
        int id, SaveMenuItemRequest request, CancellationToken ct = default)
    {
        var item = await menuRepository.FindAsync(id, ct);

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

        await unitOfWork.SaveAsync(ct);
        return ToResponse(item);
    }

    public async Task<MenuItemResponse?> SetAvailabilityAsync(
        int id, bool? isAvailable, CancellationToken ct = default)
    {
        var item = await menuRepository.FindAsync(id, ct);

        if (item is null)
        {
            return null;
        }

        if (isAvailable is null) throw new ApiException(400, "isAvailable is required.");
        item.IsAvailable = isAvailable.Value;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await unitOfWork.SaveAsync(ct);
        return ToResponse(item);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var item = await menuRepository.FindAsync(id, ct);
        if (item is null) return false;
        item.IsDeleted = true;
        await unitOfWork.SaveAsync(ct);
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

        if (ImageRules.CheckMenuImage(request.Image) is { } imageError)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, imageError);
        }

        return new ValidatedInput(name, request.CategoryId.Trim(), request.Price.Value, request.PrepTime.Value);
    }

    private async Task<Category> FindCategoryAsync(string publicCategoryId, CancellationToken ct)
    {
        if (PublicId.TryParseCategory(publicCategoryId, out var categoryId))
        {
            var category = await referenceData.FindCategoryAsync(categoryId, ct);
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

        var exists = await referenceData.UserExistsAsync(adminId.Value, ct);
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

    // first run only: copies the old node menu (Data/Seed/*.json) into the database
    public async Task SeedDevelopmentDataAsync(string contentRoot, CancellationToken ct = default)
    {
        if (await menuRepository.AnyIncludingDeletedAsync(ct))
        {
            return;
        }

        var categories = (await ReadSeedAsync(contentRoot, "categories.json")).Categories ?? [];
        var menu = (await ReadSeedAsync(contentRoot, "menu.json")).Menu ?? [];

        // the old files link items to categories by text id like "cat-burgers"
        var categoryById = new Dictionary<string, Category>();
        foreach (var c in categories)
        {
            categoryById[c.Id] = await referenceData.FindCategoryByNameAsync(c.Name, ct)
                ?? new Category { Name = c.Name, Description = c.Description ?? "" };
        }

        foreach (var category in categoryById.Values.Distinct().Where(category => category.Id == 0))
            referenceData.AddCategory(category);

        foreach (var m in menu)
        {
            menuRepository.Add(new MenuItem
            {
                Name = m.Name,
                Description = m.Description ?? "",
                Price = m.Price,
                PrepTime = m.PrepTime,
                IsAvailable = m.IsAvailable,
                SoldCount = m.SoldCount,
                Image = m.Image,
                Category = categoryById[m.CategoryId],
            });
        }
        await unitOfWork.SaveAsync(ct);
    }

    private static async Task<SeedFile> ReadSeedAsync(string contentRoot, string fileName)
    {
        await using var file = File.OpenRead(Path.Combine(contentRoot, "Data", "Seed", fileName));
        return await JsonSerializer.DeserializeAsync<SeedFile>(file, JsonSerializerOptions.Web) ?? new(null, null);
    }

    private sealed record SeedFile(List<SeedCategory>? Categories, List<SeedMenuItem>? Menu);
    private sealed record SeedCategory(string Id, string Name, string? Description);
    private sealed record SeedMenuItem(string Name, string? Description, decimal Price, string CategoryId,
        int PrepTime, bool IsAvailable, int SoldCount, string? Image);
}
