namespace FoodJS.Api.Models;

public class OrderItem : ISoftDeletable
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public int MenuItemId { get; set; }

    public MenuItem MenuItem { get; set; } = null!;

    public required string ItemName { get; set; }

    public string? SelectedChoice { get; set; }

    public int Quantity { get; set; }

    public decimal SnapshotPrice { get; set; }

    public decimal LineTotal { get; set; }

    public bool IsDeleted { get; set; }
}
