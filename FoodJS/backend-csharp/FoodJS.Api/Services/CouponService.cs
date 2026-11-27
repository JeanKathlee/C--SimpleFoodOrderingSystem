using FoodJS.Api.Contracts;
using FoodJS.Api.Repositories;
using FoodJS.Api.Common;

namespace FoodJS.Api.Services;

public sealed class CouponService(ICouponRepository repository, IUnitOfWorkRepository unitOfWork) : ICouponService
{
    private static readonly System.Text.RegularExpressions.Regex CouponCodeFormat =
        new("^[A-Z0-9]{4,12}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly DateOnly DefaultExpiryDate = new(2026, 12, 31);


    public async Task<CouponValidationResponse> ValidateAsync(
        ValidateCouponRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.OrderAmount is null || request.OrderAmount < 0)
            throw new ApiException(400, "Code and a non-negative orderAmount are required");
        var code = request.Code.Trim();
        var orderAmount = request.OrderAmount ?? 0;

        if (!CouponCodeFormat.IsMatch(code))
        {
            return Invalid("Coupon codes are case-sensitive. Use uppercase letters and numbers only.");
        }

        var coupon = await repository.FindByCodeAsync(code, cancellationToken);

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
        return (await repository.ListAsync(cancellationToken)).Select(ToResponse).ToArray();
    }

    public async Task<CouponResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var coupon = await repository.FindAsync(id, cancellationToken);
        return coupon is null ? null : ToResponse(coupon);
    }

    public async Task<CouponResponse?> CreateAsync(
        CreateCouponRequest request,
        CancellationToken cancellationToken)
    {
        ValidateDefinition(request.Code, request.Discount, request.DiscountType, request.MinOrderAmount, request.MaxUses);
        var code = request.Code!.Trim().ToUpperInvariant();
        var alreadyExists = await repository.CodeExistsAsync(code, null, cancellationToken);

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

        repository.Add(coupon);
        await unitOfWork.SaveAsync(cancellationToken);

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
        var coupon = await repository.FindAsync(id, cancellationToken);

        if (coupon is null)
        {
            return UpdateCouponResult.NotFound;
        }

        ValidateDefinition(request.Code, request.Discount, request.DiscountType, request.MinOrderAmount, request.MaxUses);
        var code = request.Code!.Trim().ToUpperInvariant();
        var alreadyExists = await repository.CodeExistsAsync(code, id, cancellationToken);

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

        await unitOfWork.SaveAsync(cancellationToken);
        return UpdateCouponResult.Updated;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var coupon = await repository.FindAsync(id, cancellationToken);

        if (coupon is null)
        {
            return false;
        }

        coupon.IsDeleted = true;
        await unitOfWork.SaveAsync(cancellationToken);
        return true;
    }
    private static void ValidateDefinition(string? code, decimal discount, string? type, decimal minimum, int? maxUses)
    {
        if (string.IsNullOrWhiteSpace(code) || discount <= 0 || string.IsNullOrWhiteSpace(type))
            throw new ApiException(400, "Code, discount, and discountType required");
        if (minimum < 0 || maxUses < -1)
            throw new ApiException(400, "Coupon amounts and usage limits cannot be negative");
        if (type.Trim() is not ("percentage" or "fixed"))
            throw new ApiException(400, "Discount type must be percentage or fixed.");
        if (type.Trim() == "percentage" && discount > 100)
            throw new ApiException(400, "Percentage discount cannot exceed 100.");
    }
}
