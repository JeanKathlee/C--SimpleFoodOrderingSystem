namespace FoodJS.Api.Models;

public class Order : ISoftDeletable
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    // Checkout snapshots: later profile/coupon edits must not change order history.
    public string? CustomerName { get; set; }

    public string? MobileNumber { get; set; }

    public string? CouponCode { get; set; }

    public int? AddressId { get; set; }

    public Address? Address { get; set; }

    public int OrderStatusId { get; set; }

    public OrderStatus OrderStatus { get; set; } = null!;

    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }

    public bool Archived { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
