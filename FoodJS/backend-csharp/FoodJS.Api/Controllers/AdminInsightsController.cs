using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using FoodJS.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FoodJS.Api.Controllers;

[ApiController]
[Route("api/admin-insights")]
[Authorize(Roles = AuthRoles.Admin)]
public sealed class AdminInsightsController(IAdminInsightsService service) : ControllerBase
{
    [HttpGet]
    public Task<AdminInsightsResponse> Get(CancellationToken ct) => service.Get(ct);
}
