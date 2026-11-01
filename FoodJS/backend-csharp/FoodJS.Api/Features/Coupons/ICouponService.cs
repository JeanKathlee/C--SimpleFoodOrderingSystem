namespace FoodJS.Api.Features.Coupons;

public interface ICouponService
{
    Task<IReadOnlyCollection<CouponResponse>> GetAllAsync(
        CancellationToken cancellationToken);

    Task<CouponResponse?> CreateAsync(
        CreateCouponRequest request,
        CancellationToken cancellationToken);
}
