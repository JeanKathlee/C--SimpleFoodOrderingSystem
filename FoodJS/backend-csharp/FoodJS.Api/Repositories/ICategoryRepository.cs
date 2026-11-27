using FoodJS.Api.Models;
namespace FoodJS.Api.Repositories;
public interface ICategoryRepository
{
    Task<List<Category>> ListAsync(CancellationToken ct);
    Task<Category?> FindAsync(int id, CancellationToken ct);
    Task<bool> HasMenuItemsAsync(int id, CancellationToken ct);
    void Add(Category category);
}
