namespace FoodJS.Api.Models;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public int MenuItemId { get; set; }

    public MenuItem MenuItem { get; set; } = null!;

    public required string ItemName { get; set; }

    public int Quantity { get; set; }

    public decimal SnapshotPrice { get; set; }

    public decimal LineTotal { get; set; }
}