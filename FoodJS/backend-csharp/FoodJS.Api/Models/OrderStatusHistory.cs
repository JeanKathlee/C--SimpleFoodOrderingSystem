namespace FoodJS.Api.Models;

public class OrderStatusHistory
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public required string FromStatus { get; set; }

    public required string ToStatus { get; set; }

    public int? ChangedByUserId { get; set; }

    public User? ChangedByUser { get; set; }

    public string Source { get; set; } = "system";

    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}