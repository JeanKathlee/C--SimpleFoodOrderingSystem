using FoodJS.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodJS.Api.Features.Orders;

[ApiController]
[Route("api/orders")]
[Authorize]
public sealed class OrdersController(IOrderService orderService) : ControllerBase
{
    // Orders of the logged-in customer, newest first.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> GetMine(CancellationToken ct) =>
        Ok(await orderService.ListForUserAsync(User.RequireUserId(), ct));

    [HttpGet("admin/all")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> GetAll(CancellationToken ct) =>
        Ok(await orderService.ListAllAsync(ct));

    // Order details + statusHistory (used by order tracking). Customers only see their own orders.
    [HttpGet("{id}")]
    public async Task<ActionResult<OrderResponse>> GetById(string id, CancellationToken ct)
    {
        var order = await orderService.GetForUserAsync(User.RequireUserId(), id, ct);
        return order is null ? NotFoundResult() : Ok(order);
    }

    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Create(CreateOrderRequest request, CancellationToken ct)
    {
        var order = await orderService.CreateAsync(User.RequireUserId(), request, ct);
        return StatusCode(StatusCodes.Status201Created, order);
    }

    [HttpPatch("{id}/cancel")]
    public async Task<ActionResult<OrderResponse>> Cancel(string id, CancellationToken ct)
    {
        var order = await orderService.CancelAsync(User.RequireUserId(), id, ct);
        return order is null ? NotFoundResult() : Ok(order);
    }

    [HttpPatch("{id}/status")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<OrderResponse>> UpdateStatus(
        string id, UpdateOrderStatusRequest request, CancellationToken ct)
    {
        var order = await orderService.UpdateStatusAsync(User.RequireUserId(), id, request, ct);
        return order is null ? NotFoundResult() : Ok(order);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct) =>
        await orderService.DeleteAsync(id, ct)
            ? Ok(new { message = "Order deleted successfully." })
            : NotFoundResult();

    private NotFoundObjectResult NotFoundResult() => NotFound(new { message = "Order not found." });
}
