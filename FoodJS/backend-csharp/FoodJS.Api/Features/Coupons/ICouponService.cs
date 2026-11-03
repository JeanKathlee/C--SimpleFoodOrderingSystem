namespace FoodJS.Api.Features.Coupons;

public interface ICouponService
{
    Task<IReadOnlyCollection<CouponResponse>> GetAllAsync(
        CancellationToken cancellationToken);

    Task<CouponResponse?> CreateAsync(
        CreateCouponRequest request,
        CancellationToken cancellationToken);

    Task<UpdateCouponResult> UpdateAsync(
        int id,
        UpdateCouponRequest request,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken);

    Task<CouponResponse?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);

    Task<CouponValidationResponse> ValidateAsync(
        ValidateCouponRequest request,
        CancellationToken cancellationToken);
}

public enum UpdateCouponResult
{
    NotFound,
    CodeAlreadyExists,
    Updated
}
