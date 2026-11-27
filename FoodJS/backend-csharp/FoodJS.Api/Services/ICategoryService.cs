using FoodJS.Api.Contracts;

namespace FoodJS.Api.Services;

public interface ICategoryService
{
    Task<List<CategoryResponse>> GetAll(CancellationToken ct);
    Task<CategoryResponse> Create(SaveCategoryRequest request, CancellationToken ct);
    Task<CategoryResponse?> Update(string id, SaveCategoryRequest request, CancellationToken ct);
    Task<bool> Delete(string id, CancellationToken ct);
}
