using FoodJS.Api.Contracts;
using FoodJS.Api.Common;
using FoodJS.Api.Models;
using FoodJS.Api.Repositories;
namespace FoodJS.Api.Services;

public sealed class CategoryService(ICategoryRepository repository, IUnitOfWorkRepository unit) : ICategoryService
{
    public async Task<List<CategoryResponse>> GetAll(CancellationToken ct) =>
        (await repository.ListAsync(ct)).Select(ToResponse).ToList();

    public async Task<CategoryResponse> Create(SaveCategoryRequest request, CancellationToken ct)
    {
        var (name, description) = Validate(request);
        var category = new Category { Name = name, Description = description };
        repository.Add(category);
        await unit.SaveAsync(ct);
        return ToResponse(category);
    }

    public async Task<CategoryResponse?> Update(string id, SaveCategoryRequest request, CancellationToken ct)
    {
        var (name, description) = Validate(request);
        var category = await FindAsync(id, ct);
        if (category is null) return null;
        category.Name = name;
        category.Description = description;
        category.UpdatedAt = DateTimeOffset.UtcNow;
        await unit.SaveAsync(ct);
        return ToResponse(category);
    }

    public async Task<bool> Delete(string id, CancellationToken ct)
    {
        var category = await FindAsync(id, ct);
        if (category is null) return false;
        if (await repository.HasMenuItemsAsync(category.Id, ct))
            throw new ApiException(409, "Only empty categories can be deleted. Move or delete its menu items first.");
        category.IsDeleted = true;
        await unit.SaveAsync(ct);
        return true;
    }

    private Task<Category?> FindAsync(string id, CancellationToken ct) =>
        PublicId.TryParseCategory(id, out var categoryId) ? repository.FindAsync(categoryId, ct) : Task.FromResult<Category?>(null);

    private static (string Name, string Description) Validate(SaveCategoryRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";
        if (name == "" || description == "") throw new ApiException(400, "Name and description are required.");
        return (name, description);
    }

    private static CategoryResponse ToResponse(Category c) => new(PublicId.Category(c.Id), c.Name, c.Description, c.CreatedAt, c.UpdatedAt);
}
