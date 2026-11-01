namespace FoodJS.Api.Features.Coupons;

public interface ICouponService
{
    Task<IReadOnlyCollection<CouponResponse>> GetAllAsync(
        CancellationToken cancellationToken);
}
