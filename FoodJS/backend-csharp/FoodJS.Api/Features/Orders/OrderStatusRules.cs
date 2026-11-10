namespace FoodJS.Api.Features.Orders;

/// <summary>Order lifecycle rules, ported 1:1 from the Node API (routes/orders.js).</summary>
public static class OrderStatusRules
{
    public const string Pending = "Pending";
    public const string Preparing = "Preparing";
    public const string Ready = "Ready";
    public const string Delivered = "Delivered";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All = [Pending, Preparing, Ready, Delivered, Cancelled];

    private static readonly Dictionary<string, string[]> Transitions = new(StringComparer.Ordinal)
    {
        [Pending] = [Preparing, Cancelled],
        [Preparing] = [Ready, Cancelled],
        [Ready] = [Delivered],
        [Delivered] = [],
        [Cancelled] = []
    };

    public static bool IsKnown(string? status) => status is not null && All.Contains(status);

    public static IReadOnlyList<string> NextStatuses(string current) =>
        Transitions.TryGetValue(current, out var next) ? next : [];

    public static bool CanTransition(string from, string to) => NextStatuses(from).Contains(to);

    /// <summary>A customer may cancel only until the kitchen has finished the order.</summary>
    public static bool CanCustomerCancel(string status) => status is Pending or Preparing;
}
