using System.ComponentModel.DataAnnotations;

namespace FoodJS.Api.Features.Orders;

public sealed class OrderPageRequest
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 50)] public int PageSize { get; init; } = 10;
    [StringLength(120)] public string? Search { get; init; }
    public string? Status { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
}

public sealed record OrderListItemResponse(string Id, string CustomerName, string Status,
    int ItemCount, decimal Total, DateTimeOffset CreatedAt);
public sealed record OrderPageResponse(IReadOnlyList<OrderListItemResponse> Items,
    int TotalCount, int Page, int PageSize, int TotalPages);

// The shape of an order item in the API response.
public sealed record OrderItemResponse(
    string MenuItemId, string Name, int Quantity, decimal Price, string? SelectedChoice = null);

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

    public string? SelectedChoice { get; init; }
}

public sealed class UpdateOrderStatusRequest
{
    public string? Status { get; init; }
}
