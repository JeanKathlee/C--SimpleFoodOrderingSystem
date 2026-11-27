using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FoodJS.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class EmailVerificationController(EmailVerificationService service) : ControllerBase
{
    [HttpPost("verify-email")]
    [EnableRateLimiting("email-verification")]
    public async Task<IActionResult> Verify(VerifyEmailRequest request) =>
        Ok(new { message = await service.VerifyAsync(request) });

    [HttpPost("resend-verification")]
    [EnableRateLimiting("email-verification")]
    public async Task<IActionResult> Resend(ResendVerificationRequest request) =>
        Ok(new { message = await service.ResendAsync(request.Email) });
}
