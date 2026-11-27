namespace FoodJS.Api.Contracts;
public sealed record CategoryResponse(string Id, string Name, string Description, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record SaveCategoryRequest(string? Name, string? Description);
