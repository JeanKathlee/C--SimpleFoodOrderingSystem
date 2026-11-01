using Microsoft.AspNetCore.Mvc;

namespace FoodJS.Api.Features.Coupons;

[ApiController]
[Route("api/coupons")]
public sealed class CouponController(ICouponService couponService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<CouponResponse>>> GetAll(
        CancellationToken cancellationToken) =>
        Ok(await couponService.GetAllAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<CouponResponse>> Create(
        CreateCouponRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) ||
            request.Discount <= 0 ||
            string.IsNullOrWhiteSpace(request.DiscountType))
        {
            return BadRequest(new
            {
                message = "Code, discount, and discountType required"
            });
        }

        if (request.MinOrderAmount < 0 || request.MaxUses < -1)
        {
            return BadRequest(new
            {
                message = "Coupon amounts and usage limits cannot be negative"
            });
        }

        var coupon = await couponService.CreateAsync(request, cancellationToken);
        return coupon is null
            ? BadRequest(new { message = "Coupon code already exists" })
            : StatusCode(StatusCodes.Status201Created, coupon);
    }
}
