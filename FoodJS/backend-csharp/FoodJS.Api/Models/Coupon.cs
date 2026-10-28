namespace FoodJS.Api.Models;

public class Coupon
{
    public int Id { get; set; }

    public required string Code { get; set; }

    public decimal Discount { get; set; }

    public required string DiscountType { get; set; }

    public decimal MinimumOrderAmount { get; set; }

    public DateOnly ExpiryDate { get; set; }

    public int? MaximumUses { get; set; }

    public int UsedCount { get; set; }

    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}