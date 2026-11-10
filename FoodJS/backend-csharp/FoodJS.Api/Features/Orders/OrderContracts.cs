namespace FoodJS.Api.Features.Orders;

// The shape of an order item in the API response.
public sealed record OrderItemResponse(string MenuItemId, string Name, int Quantity, decimal Price);

public sealed record OrderStatusHistoryResponse(
    string? FromStatus,
    string ToStatus,
    int? ActorId,
    string ActorRole,
    string Source,
    DateTimeOffset ChangedAt);

public sealed record OrderResponse(
    string Id,
    int UserId,
    string CustomerName,
    IReadOnlyList<OrderItemResponse> Items,
    int ItemCount,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    string? CouponCode,
    string PaymentMethod,
    string Address,
    string Note,
    string MobileNumber,
    string Status,
    IReadOnlyList<OrderStatusHistoryResponse> StatusHistory,
    bool Archived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// The shape of the request body for creating an order. All fields are optional to allow partial updates.
public sealed class CreateOrderRequest
{
    public List<CreateOrderItem>? Items { get; init; }

    public string? CustomerName { get; init; }

    public string? PaymentMethod { get; init; }

    public string? CouponCode { get; init; }

    public string? Address { get; init; }

    public string? Note { get; init; }

    public string? MobileNumber { get; init; }
}

public sealed class CreateOrderItem
{
    public string? MenuItemId { get; init; }

    public int Quantity { get; init; }
}

public sealed class UpdateOrderStatusRequest
{
    public string? Status { get; init; }
}
