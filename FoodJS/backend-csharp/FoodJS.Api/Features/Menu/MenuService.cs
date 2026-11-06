using System.Text.Json;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Features.Menu;

// menu items saved in the database. add, edit and delete still use InMemoryMenuService for now
public sealed class MenuService(FoodJsDbContext db)
{
    // customers only see items that are available, admins see everything (so they can turn items back on).
    // search looks in the name and description, category is the category name like "Burgers"
    public async Task<List<MenuItemResponse>> GetAllAsync(bool showHidden, string? search = null, string? category = null)
    {
        var query = Visible(showHidden);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim().ToLower();
            query = query.Where(m => m.Name.ToLower().Contains(text) || m.Description.ToLower().Contains(text));
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            var name = category.Trim().ToLower();
            query = query.Where(m => m.Category.Name.ToLower() == name);
        }

        var items = await query.OrderBy(m => m.Id).ToListAsync();
        return items.Select(ToResponse).ToList();
    }

    // one food item, null if it doesn't exist (or is hidden and you're not an admin)
    public async Task<MenuItemResponse?> GetAsync(int id, bool showHidden)
    {
        var item = await Visible(showHidden).FirstOrDefaultAsync(m => m.Id == id);
        return item is null ? null : ToResponse(item);
    }

    private IQueryable<MenuItem> Visible(bool showHidden) =>
        db.MenuItems.Include(m => m.Category).Where(m => showHidden || m.IsAvailable);

    private static MenuItemResponse ToResponse(MenuItem m) => new(
        m.Id.ToString(), m.Name, m.Description, m.CategoryId.ToString(), m.Category.Name,
        m.Price, m.PrepTime, m.IsAvailable, m.SoldCount, m.Image, m.CreatedAt, m.UpdatedAt);

    // first run only: copies the old node menu (Data/Seed/*.json) into the database
    public static async Task SeedDevelopmentDataAsync(FoodJsDbContext db, string contentRoot)
    {
        if (await db.MenuItems.AnyAsync())
        {
            return;
        }

        var categories = (await ReadSeedAsync(contentRoot, "categories.json")).Categories ?? [];
        var menu = (await ReadSeedAsync(contentRoot, "menu.json")).Menu ?? [];

        // the old files link items to categories by text id like "cat-burgers"
        var categoryById = new Dictionary<string, Category>();
        foreach (var c in categories)
        {
            categoryById[c.Id] = await db.Categories.FirstOrDefaultAsync(x => x.Name == c.Name)
                ?? db.Categories.Add(new Category { Name = c.Name, Description = c.Description ?? "" }).Entity;
        }

        foreach (var m in menu)
        {
            db.MenuItems.Add(new MenuItem
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
        await db.SaveChangesAsync();
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
