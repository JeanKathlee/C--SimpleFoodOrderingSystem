using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Repositories;

public sealed class MenuRepository(FoodJsDbContext db) : IMenuRepository
{
    public async Task<IReadOnlyList<MenuItem>> ListAsync(bool includeUnavailable, string? search, string? category, CancellationToken ct = default)
    {
        var query = db.MenuItems.AsNoTracking().Include(item => item.Category).AsQueryable();
        if (!includeUnavailable) query = query.Where(item => item.IsAvailable);
        if (search is not null) query = query.Where(item => item.Name.ToLower().Contains(search) || item.Description.ToLower().Contains(search));
        if (category is not null) query = query.Where(item => item.Category.Name.ToLower() == category);
        return await query.OrderBy(item => item.Id).ToListAsync(ct);
    }

    public Task<MenuItem?> FindAsync(int id, CancellationToken ct = default) =>
        db.MenuItems.Include(item => item.Category).FirstOrDefaultAsync(item => item.Id == id, ct);

    public async Task<IReadOnlyList<MenuItem>> FindManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default) =>
        await db.MenuItems.Where(item => ids.Contains(item.Id)).ToListAsync(ct);

    public Task<bool> AnyIncludingDeletedAsync(CancellationToken ct = default) =>
        db.MenuItems.IgnoreQueryFilters().AnyAsync(ct);

    public void Add(MenuItem item) => db.MenuItems.Add(item);
}
