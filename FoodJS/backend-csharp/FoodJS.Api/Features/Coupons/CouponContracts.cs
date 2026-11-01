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
