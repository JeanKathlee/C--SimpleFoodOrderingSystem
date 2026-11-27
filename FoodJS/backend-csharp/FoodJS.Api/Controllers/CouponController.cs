using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using FoodJS.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodJS.Api.Controllers;

[ApiController]
[Route("api/coupons")]
public sealed class CouponController(ICouponService couponService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<CouponResponse>>> GetAll(
        CancellationToken cancellationToken) =>
        Ok(await couponService.GetAllAsync(cancellationToken));

    [HttpPost("validate")]
    public async Task<ActionResult<CouponValidationResponse>> Validate(
        ValidateCouponRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await couponService.ValidateAsync(request, cancellationToken));
    }

    [HttpPost]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<CouponResponse>> Create(
        CreateCouponRequest request,
        CancellationToken cancellationToken)
    {
        var coupon = await couponService.CreateAsync(request, cancellationToken);
        return coupon is null
            ? BadRequest(new { message = "Coupon code already exists" })
            : StatusCode(StatusCodes.Status201Created, coupon);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<CouponResponse>> Update(
        int id,
        UpdateCouponRequest request,
        CancellationToken cancellationToken)
    {
        var result = await couponService.UpdateAsync(id, request, cancellationToken);
        return result switch
        {
            UpdateCouponResult.NotFound => NotFound(new { message = "Coupon not found" }),
            UpdateCouponResult.CodeAlreadyExists =>
                BadRequest(new { message = "Coupon code already exists" }),
            _ => Ok(await couponService.GetByIdAsync(id, cancellationToken))
        };
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        var deleted = await couponService.DeleteAsync(id, cancellationToken);
        return deleted
            ? Ok(new { message = "Coupon deleted successfully." })
            : NotFound(new { message = "Coupon not found." });
    }
}
