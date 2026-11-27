namespace FoodJS.Api.Models;

public class MenuItem : ISoftDeletable
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int PrepTime { get; set; }

    public bool IsAvailable { get; set; } = true;

    public bool IsDeleted { get; set; }

    public int SoldCount { get; set; }

    public string? Image { get; set; }

    // Null/unconfigured and [] both mean standalone. Only explicit options include a drink.
    public string? DrinkOptionsJson { get; set; }

    public int CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public int? CreatedByAdminId { get; set; }

    public User? CreatedByAdmin { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
