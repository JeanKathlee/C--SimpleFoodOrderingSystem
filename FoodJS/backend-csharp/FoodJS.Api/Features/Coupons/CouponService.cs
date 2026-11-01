using FoodJS.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Features.Coupons;

public sealed class CouponService(FoodJsDbContext dbContext) : ICouponService
{
    private static readonly DateOnly DefaultExpiryDate = new(2026, 12, 31);

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

    public async Task<CouponResponse?> CreateAsync(
        CreateCouponRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.Code!.Trim().ToUpperInvariant();
        var alreadyExists = await dbContext.Coupons
            .AnyAsync(coupon => coupon.Code.ToUpper() == code, cancellationToken);

        if (alreadyExists)
        {
            return null;
        }

        var coupon = new Models.Coupon
        {
            Code = code,
            Discount = request.Discount,
            DiscountType = request.DiscountType!.Trim(),
            MinimumOrderAmount = request.MinOrderAmount,
            ExpiryDate = request.ExpiryDate ?? DefaultExpiryDate,
            MaximumUses = request.MaxUses ?? -1,
            Description = request.Description?.Trim() ?? string.Empty
        };

        dbContext.Coupons.Add(coupon);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CouponResponse(
            coupon.Id,
            coupon.Code,
            coupon.Discount,
            coupon.DiscountType,
            coupon.MinimumOrderAmount,
            coupon.ExpiryDate,
            coupon.MaximumUses ?? -1,
            coupon.UsedCount,
            coupon.Description);
    }
}
