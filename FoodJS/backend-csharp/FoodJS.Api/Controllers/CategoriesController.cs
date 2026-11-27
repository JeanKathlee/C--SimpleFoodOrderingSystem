using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using FoodJS.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FoodJS.Api.Controllers;

[ApiController]
[Route("api/categories")]
public sealed class CategoriesController(ICategoryService service) : ControllerBase
{
    [HttpGet]
    public Task<List<CategoryResponse>> GetAll(CancellationToken ct) => service.GetAll(ct);

    [HttpPost]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Create(SaveCategoryRequest request, CancellationToken ct) =>
        StatusCode(201, await service.Create(request, ct));

    [HttpPut("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Update(string id, SaveCategoryRequest request, CancellationToken ct)
    {
        var category = await service.Update(id, request, ct);
        return category is null ? NotFound(new { message = "Category not found." }) : Ok(category);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct) =>
        await service.Delete(id, ct) ? Ok(new { message = "Category deleted successfully." }) : NotFound(new { message = "Category not found." });
}
