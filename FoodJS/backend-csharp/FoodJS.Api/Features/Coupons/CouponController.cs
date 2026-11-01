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
}
