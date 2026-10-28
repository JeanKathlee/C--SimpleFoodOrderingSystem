namespace FoodJS.Api.Models;

public class Category
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}