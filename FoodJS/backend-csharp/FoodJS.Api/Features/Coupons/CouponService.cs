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

    public async Task<CouponResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.Coupons
            .AsNoTracking()
            .Where(coupon => coupon.Id == id)
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
            .SingleOrDefaultAsync(cancellationToken);
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

    public async Task<UpdateCouponResult> UpdateAsync(
        int id,
        UpdateCouponRequest request,
        CancellationToken cancellationToken)
    {
        var coupon = await dbContext.Coupons
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (coupon is null)
        {
            return UpdateCouponResult.NotFound;
        }

        var code = request.Code!.Trim().ToUpperInvariant();
        var alreadyExists = await dbContext.Coupons
            .AnyAsync(candidate => candidate.Id != id &&
                candidate.Code.ToUpper() == code, cancellationToken);

        if (alreadyExists)
        {
            return UpdateCouponResult.CodeAlreadyExists;
        }

        coupon.Code = code;
        coupon.Discount = request.Discount;
        coupon.DiscountType = request.DiscountType!.Trim();
        coupon.MinimumOrderAmount = request.MinOrderAmount;
        coupon.MaximumUses = request.MaxUses ?? -1;
        coupon.Description = request.Description?.Trim() ?? string.Empty;

        if (request.ExpiryDate is not null)
        {
            coupon.ExpiryDate = request.ExpiryDate.Value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return UpdateCouponResult.Updated;
    }
}
