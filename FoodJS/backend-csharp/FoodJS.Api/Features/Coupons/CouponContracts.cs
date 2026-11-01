namespace FoodJS.Api.Features.Coupons;

public sealed record CouponResponse(
    int Id,
    string Code,
    decimal Discount,
    string DiscountType,
    decimal MinOrderAmount,
    DateOnly ExpiryDate,
    int MaxUses,
    int UsedCount,
    string Description);

public sealed class CreateCouponRequest
{
    public string? Code { get; init; }

    public decimal Discount { get; init; }

    public string? DiscountType { get; init; }

    public decimal MinOrderAmount { get; init; }

    public DateOnly? ExpiryDate { get; init; }

    public int? MaxUses { get; init; }

    public string? Description { get; init; }
}
