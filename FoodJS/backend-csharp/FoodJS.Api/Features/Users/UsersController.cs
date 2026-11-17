using FoodJS.Api.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodJS.Api.Features.Users;

// same urls as the old node api (backend/routes/users.js). login is in AuthController
[ApiController]
[Route("api/users")]
public sealed class UsersController(UsersService usersService) : ControllerBase
{
    // list of all users, admin only (the old node api showed this to everyone)
    [HttpGet]
    [Authorize(Roles = AuthService.AdminRole)]
    public async Task<IActionResult> GetAll() => Ok(await usersService.GetAllAsync());

    [HttpPost("register")]
    public Task<IActionResult> Register(RegisterRequest request) => SendErrorsBack(async () =>
        StatusCode(StatusCodes.Status201Created, await usersService.RegisterAsync(request)));

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe()
    {
        var user = await usersService.GetAsync(User.GetUserId());
        return user is null ? NotFound(new { message = "User not found." }) : Ok(user);
    }

    [HttpPatch("me")]
    [Authorize]
    public Task<IActionResult> UpdateMe(UpdateProfileRequest request) => SendErrorsBack(async () =>
    {
        var user = await usersService.UpdateAsync(User.GetUserId(), request);
        return user is null ? NotFound(new { message = "User not found." }) : Ok(user);
    });

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AuthService.AdminRole)]
    public Task<IActionResult> Delete(int id) => SendErrorsBack(async () =>
        await usersService.DeleteAsync(id, User.GetUserId())
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
