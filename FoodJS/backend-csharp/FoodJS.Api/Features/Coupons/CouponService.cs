using FoodJS.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace FoodJS.Api.Features.Coupons;

public sealed class CouponService(FoodJsDbContext dbContext) : ICouponService
{
    private static readonly System.Text.RegularExpressions.Regex CouponCodeFormat =
        new("^[A-Z0-9]{4,12}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly DateOnly DefaultExpiryDate = new(2026, 12, 31);

    private static readonly Expression<Func<Models.Coupon, CouponResponse>> CouponResponseProjection =
        coupon => new CouponResponse(
            coupon.Id,
            coupon.Code,
            coupon.Discount,
            coupon.DiscountType,
            coupon.MinimumOrderAmount,
            coupon.ExpiryDate,
            coupon.MaximumUses ?? -1,
            coupon.UsedCount,
            coupon.Description);

    public async Task<CouponValidationResponse> ValidateAsync(
        ValidateCouponRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.Code?.Trim() ?? string.Empty;
        var orderAmount = request.OrderAmount ?? 0;

        if (!CouponCodeFormat.IsMatch(code))
        {
            return Invalid("Coupon codes are case-sensitive. Use uppercase letters and numbers only.");
        }

        var coupon = await dbContext.Coupons
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Code == code, cancellationToken);

        if (coupon is null)
        {
            return Invalid("Coupon not found");
        }

        if (!coupon.IsActive)
        {
            return Invalid("Coupon inactive");
        }

        if (DateOnly.FromDateTime(DateTime.UtcNow) > coupon.ExpiryDate)
        {
            return Invalid("Coupon expired");
        }

        if (coupon.MaximumUses is >= 0 && coupon.UsedCount >= coupon.MaximumUses)
        {
            return Invalid("Coupon usage limit reached");
        }

        if (orderAmount < coupon.MinimumOrderAmount)
        {
            return Invalid($"Minimum order amount {coupon.MinimumOrderAmount} required");
        }

        var discountAmount = coupon.DiscountType switch
        {
            "percentage" => orderAmount * coupon.Discount / 100,
            "fixed" => coupon.Discount,
            _ => 0
        };

        return new CouponValidationResponse(
            true,
            null,
            ToResponse(coupon),
            discountAmount,
            Math.Max(0, orderAmount - discountAmount));
    }

    public async Task<IReadOnlyCollection<CouponResponse>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.Coupons
            .AsNoTracking()
            .OrderBy(coupon => coupon.Id)
            .Select(CouponResponseProjection)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<CouponResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await dbContext.Coupons
            .AsNoTracking()
            .Where(coupon => coupon.Id == id)
            .Select(CouponResponseProjection)
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

    private static CouponResponse ToResponse(Models.Coupon coupon) =>
        new(
            coupon.Id,
            coupon.Code,
            coupon.Discount,
            coupon.DiscountType,
            coupon.MinimumOrderAmount,
            coupon.ExpiryDate,
            coupon.MaximumUses ?? -1,
            coupon.UsedCount,
            coupon.Description);

    private static CouponValidationResponse Invalid(string error) =>
        new(false, error, null, 0, 0);

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

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var coupon = await dbContext.Coupons
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (coupon is null)
        {
            return false;
        }

        dbContext.Coupons.Remove(coupon);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
