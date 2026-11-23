using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Features.Categories;

public sealed record CategoryResponse(string Id, string Name, string Description, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record SaveCategoryRequest(string? Name, string? Description);

// same urls as the old node api (backend/routes/categories.js)
[ApiController]
[Route("api/categories")]
public sealed class CategoriesController(FoodJsDbContext db) : ControllerBase
{
    // public: the menu and the dashboard load it without a token
    [HttpGet]
    public async Task<List<CategoryResponse>> GetAll(CancellationToken ct) =>
        (await db.Categories.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct)).Select(ToResponse).ToList();

    [HttpPost]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Create(SaveCategoryRequest request, CancellationToken ct)
    {
        var (name, description) = Validate(request);
        var category = new Category { Name = name, Description = description };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return StatusCode(StatusCodes.Status201Created, ToResponse(category));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Update(string id, SaveCategoryRequest request, CancellationToken ct)
    {
        var (name, description) = Validate(request);
        var category = await FindAsync(id, ct);
        if (category is null)
        {
            return NotFoundResult();
        }

        category.Name = name;
        category.Description = description;
        category.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(ToResponse(category));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var category = await FindAsync(id, ct);
        if (category is null)
        {
            return NotFoundResult();
        }

        // MenuItems.CategoryId is ON DELETE CASCADE, so deleting a used category would wipe its menu items
        if (await db.MenuItems.AnyAsync(m => m.CategoryId == category.Id, ct))
        {
            return Conflict(new { message = "Only empty categories can be deleted. Move or delete its menu items first." });
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Category deleted successfully." });
    }

    private async Task<Category?> FindAsync(string id, CancellationToken ct) =>
        PublicId.TryParseCategory(id, out var categoryId)
            ? await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId, ct)
            : null;

    private static (string Name, string Description) Validate(SaveCategoryRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";
        if (name == "" || description == "")
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "Name and description are required.");
        }
        return (name, description);
    }

    private static CategoryResponse ToResponse(Category c) =>
        new(PublicId.Category(c.Id), c.Name, c.Description, c.CreatedAt, c.UpdatedAt);

    private NotFoundObjectResult NotFoundResult() => NotFound(new { message = "Category not found." });
}
