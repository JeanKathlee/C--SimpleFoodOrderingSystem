using FoodJS.Api.Common;
using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace FoodJS.Api.Controllers;

[ApiController]
public sealed class AuthController(AuthService service, PasswordResetService passwordReset) : ControllerBase
{
    [HttpPost("api/users/login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var result = await service.LoginAsync(request.Email, request.Password);
        return result is null ? Unauthorized(new { message = "Invalid email or password." }) : Ok(result);
    }

    [HttpPost("api/auth/google/callback")]
    public async Task<IActionResult> GoogleCallback(GoogleCallbackRequest request) =>
        Ok(await service.GoogleLoginAsync(request.Code, request.RedirectUri));

    [HttpPost("api/auth/forgot-password")]
    [EnableRateLimiting("password-recovery")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request) =>
        Ok(await passwordReset.RequestAsync(request.Email));

    [HttpPost("api/auth/reset-password")]
    [EnableRateLimiting("password-recovery")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request) =>
        await passwordReset.ResetAsync(request.Email, request.Token, request.NewPassword)
            ? Ok(new { message = "Password reset successfully. You can now sign in." })
            : BadRequest(new { message = "The password-reset link is invalid or expired." });
}
