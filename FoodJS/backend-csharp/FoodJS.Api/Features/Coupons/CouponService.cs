using FoodJS.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Features.Coupons;

public sealed class CouponService(FoodJsDbContext dbContext) : ICouponService
{
    public async Task<IReadOnlyCollection<CouponResponse>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.Coupons
            .AsNoTracking()
            .OrderBy(coupon => coupon.Id)
            .Select(coupon => new CouponResponse(
                coupon.Id,
                coupon.Code,
                coupon.Discount,
                coupon.DiscountType,
                coupon.MinimumOrderAmount,
                coupon.ExpiryDate,
                coupon.MaximumUses ?? -1,
                coupon.UsedCount,
                coupon.Description))
            .ToArrayAsync(cancellationToken);
    }
}
