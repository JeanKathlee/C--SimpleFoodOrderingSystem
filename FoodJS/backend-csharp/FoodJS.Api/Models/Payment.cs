namespace FoodJS.Api.Models;

public class Payment
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public required string PaymentMethod { get; set; }

    public decimal Amount { get; set; }

    public string PaymentStatus { get; set; } = "Pending";

    public DateTimeOffset? PaidAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}