namespace FoodJS.Api.Models;

public class Order
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public int? AddressId { get; set; }

    public Address? Address { get; set; }

    public int OrderStatusId { get; set; }

    public OrderStatus OrderStatus { get; set; } = null!;

    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }

    public bool Archived { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}