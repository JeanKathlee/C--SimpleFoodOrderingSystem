using FoodJS.Api.Contracts;
using FoodJS.Api.Repositories;
using FoodJS.Api.Models;

namespace FoodJS.Api.Services;

public sealed class CouponSeeder(ICouponRepository repository, IUnitOfWorkRepository unit)
{
    public async Task EnsureDevelopmentCouponsAsync(
        CancellationToken cancellationToken = default)
    {
        if (await repository.AnyIncludingDeletedAsync(cancellationToken))
        {
            return;
        }

        foreach (var coupon in new[]
        {
            new Coupon
            {
                Code = "PALDO10",
                Discount = 10,
                DiscountType = "percentage",
                MinimumOrderAmount = 100,
                ExpiryDate = new DateOnly(2026, 12, 31),
                MaximumUses = -1,
                Description = "10% off"
            },
            new Coupon
            {
                Code = "PALDO50",
                Discount = 50,
                DiscountType = "fixed",
                MinimumOrderAmount = 200,
                ExpiryDate = new DateOnly(2026, 12, 31),
                MaximumUses = -1,
                Description = "Flat 50 off on orders above 200"
            },
            new Coupon
            {
                Code = "PALDO20",
                Discount = 20,
                DiscountType = "percentage",
                MinimumOrderAmount = 0,
                ExpiryDate = new DateOnly(2026, 12, 31),
                MaximumUses = 1,
                Description = "20% off on first order"
            }
        }) repository.Add(coupon);

        await unit.SaveAsync(cancellationToken);
    }
}
