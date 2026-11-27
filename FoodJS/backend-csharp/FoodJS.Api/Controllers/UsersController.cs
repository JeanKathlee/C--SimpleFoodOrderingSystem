using FoodJS.Api.Common;
using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FoodJS.Api.Controllers;

// Customer account endpoints. Sign-in is handled by AuthController.
[ApiController]
[Route("api/users")]
public sealed class UsersController(UsersService usersService) : ControllerBase
{
    // list of all users, admin only (the old node api showed this to everyone)
    [HttpGet]
    [Authorize(Roles = AuthService.AdminRole)]
    public async Task<IActionResult> GetAll() => Ok(await usersService.GetAllAsync());

    [HttpPost("register")]
    [EnableRateLimiting("email-verification")]
    public Task<IActionResult> Register(RegisterRequest request) => SendErrorsBack(async () =>
        StatusCode(StatusCodes.Status201Created, await usersService.RegisterAsync(request)));

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe()
    {
        var user = await usersService.GetAsync(User.RequireUserId());
        return user is null ? NotFound(new { message = "User not found." }) : Ok(user);
    }

    [HttpPatch("me")]
    [Authorize]
    public Task<IActionResult> UpdateMe(UpdateProfileRequest request) => SendErrorsBack(async () =>
    {
        var user = await usersService.UpdateAsync(User.RequireUserId(), request);
        return user is null ? NotFound(new { message = "User not found." }) : Ok(user);
    });

    // admin edits a customer's profile from the Customers panel
    [HttpPatch("{id:int}")]
    [Authorize(Roles = AuthService.AdminRole)]
    public Task<IActionResult> UpdateCustomer(int id, UpdateProfileRequest request) => SendErrorsBack(async () =>
    {
        var user = await usersService.UpdateCustomerAsync(id, request);
        return user is null ? NotFound(new { message = "Customer not found." }) : Ok(user);
    });

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AuthService.AdminRole)]
    public Task<IActionResult> Delete(int id) => SendErrorsBack(async () =>
        await usersService.DeleteAsync(id, User.RequireUserId())
            ? Ok(new { message = "User deleted successfully." })
            : NotFound(new { message = "User not found." }));

    // turns the service's errors into { message } like the old node api
    private async Task<IActionResult> SendErrorsBack(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (BadHttpRequestException error)
        {
            return StatusCode(error.StatusCode, new { message = error.Message });
        }
    }
}
