using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;
namespace FoodJS.Api.Repositories;
public sealed class CategoryRepository(FoodJsDbContext db) : ICategoryRepository
{
    public Task<List<Category>> ListAsync(CancellationToken ct) => db.Categories.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);
    public Task<Category?> FindAsync(int id, CancellationToken ct) => db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
    public Task<bool> HasMenuItemsAsync(int id, CancellationToken ct) => db.MenuItems.AnyAsync(m => m.CategoryId == id, ct);
    public void Add(Category category) => db.Categories.Add(category);
}
