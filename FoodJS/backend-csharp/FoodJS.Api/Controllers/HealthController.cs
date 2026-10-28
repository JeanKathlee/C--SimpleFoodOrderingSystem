using Microsoft.AspNetCore.Mvc;

namespace FoodJS.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(IHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        service = "FoodJS C# API",
        environment = environment.EnvironmentName
    });
}
