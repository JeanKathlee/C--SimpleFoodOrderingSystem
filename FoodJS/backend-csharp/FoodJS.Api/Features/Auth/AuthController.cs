using Microsoft.AspNetCore.Mvc;

namespace FoodJS.Api.Features.Auth;

// same urls as the old node api, so the react app doesn't need changes
// no logout here, the react app just deletes the saved token
[ApiController]
public sealed class AuthController(AuthService authService, IConfiguration config) : ControllerBase
{
    [HttpPost("api/users/login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(new { message = "Email and password are required." });
        }

        var result = await authService.LoginAsync(request.Email.Trim(), request.Password);
        return result is null
            ? Unauthorized(new { message = "Invalid email or password." })
            : Ok(result);
    }

    [HttpPost("api/auth/google/callback")]
    public async Task<IActionResult> GoogleCallback(GoogleCallbackRequest request)
    {
        if (string.IsNullOrEmpty(request.Code))
        {
            return BadRequest(new { message = "Authorization code is required" });
        }

        try
        {
            var redirectUri = request.RedirectUri ?? config["Google:RedirectUri"]!;
            return Ok(await authService.GoogleLoginAsync(request.Code, redirectUri));
        }
        catch (BadHttpRequestException error)
        {
            return StatusCode(error.StatusCode, new { message = error.Message });
        }
    }
}
