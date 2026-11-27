using FoodJS.Api.Services;
using Microsoft.AspNetCore.Mvc;
namespace FoodJS.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(HealthService service) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(service.Get());
}
