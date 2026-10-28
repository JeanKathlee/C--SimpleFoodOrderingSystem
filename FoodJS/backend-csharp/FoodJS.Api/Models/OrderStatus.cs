namespace FoodJS.Api.Models;

public class OrderStatus
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;
}
