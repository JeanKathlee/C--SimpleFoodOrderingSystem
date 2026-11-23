using FoodJS.Api.Common;
using FoodJS.Api.Repositories;
using FoodJS.Api.Features.Checkout;
using FoodJS.Api.Models;

namespace FoodJS.Api.Features.Orders;

public sealed class OrderService(IOrderRepository orders, IMenuRepository menu, IReferenceDataRepository referenceData, IUnitOfWorkRepository unitOfWork) : IOrderService
{
    // Addresses.City is required by the schema, but checkout only collects one free-text
    // address (map picker / typed). The full text goes into AddressLine1.
    private const string UnspecifiedCity = "N/A";
    private const string DefaultPaymentMethod = "cash";

    // ---- Order Management: List / View / Track --------------------------------------

    public async Task<IReadOnlyList<OrderResponse>> ListForUserAsync(int userId, CancellationToken ct = default) =>
        await BuildResponsesAsync(userId, null, ct);

    public async Task<IReadOnlyList<OrderResponse>> ListAllAsync(CancellationToken ct = default) =>
        await BuildResponsesAsync(null, null, ct);

    public async Task<OrderResponse?> GetForUserAsync(int userId, string publicId, CancellationToken ct = default)
    {
        if (!PublicId.TryParseOrder(publicId, out var id))
        {
            return null;
        }

        var responses = await BuildResponsesAsync(userId, id, ct);
        return responses.FirstOrDefault();
    }

    // ---- Checkout: Place Order (+ Add Address, Apply Coupon) --------------------------

    public async Task<OrderResponse> CreateAsync(int userId, CreateOrderRequest request, CancellationToken ct = default)
    {
        if (request.Items is not { Count: > 0 })
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "Items are required.");
        }

        var customerName = request.CustomerName?.Trim();
        if (string.IsNullOrEmpty(customerName))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "Customer name is required.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        var user = await referenceData.FindUserAsync(userId, ct)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Unauthorized. User not found.");

        // 1. Price every line from the menu, never from the client.
        var quantities = MergeLines(request.Items);
        var menuIds = quantities.Keys.ToList();
        var menuItems = (await menu.FindManyAsync(menuIds, ct)).ToDictionary(item => item.Id);

        var lines = new List<(MenuItem Item, int Quantity, decimal LineTotal)>();
        foreach (var (menuId, quantity) in quantities)
        {
            if (!menuItems.TryGetValue(menuId, out var item))
            {
                throw new ApiException(
                    StatusCodes.Status400BadRequest,
                    $"Menu item {PublicId.Menu(menuId)} no longer exists.");
            }

            if (!item.IsAvailable)
            {
                throw new ApiException(
                    StatusCodes.Status400BadRequest,
                    $"{item.Name} is currently unavailable.");
            }

            lines.Add((item, quantity, Math.Round(item.Price * quantity, 2)));
        }

        var subtotal = lines.Sum(line => line.LineTotal);

        // 2. Re-validate the coupon on the server and compute the discount ourselves.
        Coupon? coupon = null;
        var discount = 0m;
        var couponCode = request.CouponCode?.Trim();
        if (!string.IsNullOrEmpty(couponCode))
        {
            coupon = await referenceData.FindCouponAsync(couponCode, ct);
            var check = CouponRules.Evaluate(couponCode, coupon, subtotal, DateOnly.FromDateTime(DateTime.UtcNow));
            if (!check.Valid)
            {
                throw new ApiException(StatusCodes.Status400BadRequest, check.Error!);
            }

            discount = check.DiscountAmount;
        }

        var total = Math.Max(0m, subtotal - discount);

        // 3. Delivery address: reuse an identical saved address, otherwise save a new one.
        var address = await FindOrCreateAddressAsync(userId, request.Address, ct);

        var pendingStatus = await GetStatusRowAsync(OrderStatusRules.Pending, ct);
        var now = DateTimeOffset.UtcNow;

        var order = new Order
        {
            UserId = userId,
            Address = address,
            OrderStatusId = pendingStatus.Id,
            TotalAmount = total,
            Notes = request.Note?.Trim() ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };
        orders.Add(order);

        orders.AddItems(lines.Select(line => new OrderItem
        {
            Order = order,
            MenuItemId = line.Item.Id,
            ItemName = line.Item.Name,
            Quantity = line.Quantity,
            SnapshotPrice = line.Item.Price,
            LineTotal = line.LineTotal
        }));

        orders.AddPayment(new Payment
        {
            Order = order,
            PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod)
                ? DefaultPaymentMethod
                : request.PaymentMethod.Trim(),
            Amount = total,
            PaymentStatus = "Pending"
        });

        orders.AddHistory(new OrderStatusHistory
        {
            Order = order,
            FromStatus = string.Empty,
            ToStatus = OrderStatusRules.Pending,
            ChangedByUserId = userId,
            Source = "customer-create",
            ChangedAt = now
        });

        // Save once so the order has its generated id for the notification text.
        await unitOfWork.SaveAsync(ct);

        orders.AddNotification(NewStatusNotification(userId, order.Id, OrderStatusRules.Pending, now));

        if (coupon is not null)
        {
            coupon.UsedCount += 1;
        }

        // Remember the contact number the first time a customer orders without one on file.
        var mobile = request.MobileNumber?.Trim();
        if (!string.IsNullOrEmpty(mobile) && string.IsNullOrWhiteSpace(user.MobileNumber))
        {
            user.MobileNumber = mobile;
        }

        await unitOfWork.SaveAsync(ct);
        await transaction.CommitAsync(ct);

        return await GetSingleAsync(order.Id, ct);
    }

    // ---- Order Management: Cancel / Edit Status / Delete ------------------------------

    public async Task<OrderResponse?> CancelAsync(int userId, string publicId, CancellationToken ct = default)
    {
        if (!PublicId.TryParseOrder(publicId, out var id))
        {
            return null;
        }

        var order = await orders.FindAsync(id, userId, ct);
        if (order is null)
        {
            return null;
        }

        var current = order.OrderStatus.Name;
        if (!OrderStatusRules.CanCustomerCancel(current))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, $"Cannot cancel order with status: {current}");
        }

        await ApplyStatusAsync(order, current, OrderStatusRules.Cancelled, userId, "customer-cancel", ct);
        return await GetSingleAsync(order.Id, ct);
    }

    public async Task<OrderResponse?> UpdateStatusAsync(
        int adminId, string publicId, UpdateOrderStatusRequest request, CancellationToken ct = default)
    {
        if (!PublicId.TryParseOrder(publicId, out var id))
        {
            return null;
        }

        var order = await orders.FindAsync(id, ct: ct);
        if (order is null)
        {
            return null;
        }

        var next = request.Status;
        if (string.IsNullOrEmpty(next))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "Status is required.");
        }

        if (!OrderStatusRules.IsKnown(next))
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                $"Invalid status. Allowed values: {string.Join(", ", OrderStatusRules.All)}");
        }

        var current = order.OrderStatus.Name;
        if (current == next)
        {
            return await GetSingleAsync(order.Id, ct);
        }

        if (!OrderStatusRules.CanTransition(current, next))
        {
            var allowed = OrderStatusRules.NextStatuses(current);
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                $"Invalid status transition from {current} to {next}. " +
                $"Allowed next status: {(allowed.Count == 0 ? "none" : string.Join(", ", allowed))}");
        }

        await ApplyStatusAsync(order, current, next, adminId, "admin-update", ct);
        return await GetSingleAsync(order.Id, ct);
    }

    public async Task<bool> DeleteAsync(string publicId, CancellationToken ct = default)
    {
        if (!PublicId.TryParseOrder(publicId, out var id))
        {
            return false;
        }

        var order = await orders.FindAsync(id, ct: ct);
        if (order is null) return false;
        order.IsDeleted = true;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveAsync(ct);
        return true;
    }

    // ---- helpers --------------------------------------------------------------------

    private async Task ApplyStatusAsync(
        Order order, string from, string to, int actorId, string source, CancellationToken ct)
    {
        var statusRow = await GetStatusRowAsync(to, ct);
        var now = DateTimeOffset.UtcNow;

        order.OrderStatusId = statusRow.Id;
        order.OrderStatus = statusRow;
        order.UpdatedAt = now;

        orders.AddHistory(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = from,
            ToStatus = to,
            // ChangedByUserId is a foreign key; skip it when the token's user is not in the table.
            ChangedByUserId = await referenceData.UserExistsAsync(actorId, ct) ? actorId : null,
            Source = source,
            ChangedAt = now
        });

        orders.AddNotification(NewStatusNotification(order.UserId, order.Id, to, now));
        await unitOfWork.SaveAsync(ct);
    }

    // The notifications API (coupons/notifications owner) reads these rows. Type carries the
    // status name so it can be returned as the Node "status" field.
    private static Notification NewStatusNotification(int userId, int orderId, string status, DateTimeOffset now) => new()
    {
        UserId = userId,
        OrderId = orderId,
        Title = "Order status updated",
        Message = $"Order {PublicId.Order(orderId)} status updated to {status}.",
        Type = status,
        IsRead = false,
        CreatedAt = now
    };

    private async Task<OrderStatus> GetStatusRowAsync(string name, CancellationToken ct) =>
        await referenceData.FindStatusAsync(name, ct)
        ?? throw new InvalidOperationException(
            $"Order status '{name}' is missing. Run OrderStatusSeeder.EnsureSeededAsync at startup.");

    private async Task<Address?> FindOrCreateAddressAsync(int userId, string? rawAddress, CancellationToken ct)
    {
        var text = rawAddress?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var lowered = text.ToLower();
        var existing = await referenceData.FindAddressAsync(userId, lowered, ct);
        if (existing is not null)
        {
            return existing;
        }

        var created = new Address
        {
            UserId = userId,
            AddressLine1 = text,
            City = UnspecifiedCity,
            IsDefault = false
        };
        referenceData.AddAddress(created);
        return created;
    }

    // Same product on several lines is merged into one line, like the cart does.
    private static Dictionary<int, int> MergeLines(IEnumerable<CreateOrderItem?> items)
    {
        var merged = new Dictionary<int, int>();
        foreach (var item in items)
        {
            if (item is null || !PublicId.TryParseMenu(item.MenuItemId, out var menuId))
            {
                throw new ApiException(
                    StatusCodes.Status400BadRequest,
                    $"Invalid menu item: {item?.MenuItemId ?? "(missing)"}.");
            }

            if (item.Quantity <= 0)
            {
                throw new ApiException(StatusCodes.Status400BadRequest, "Item quantity must be greater than 0.");
            }

            merged[menuId] = merged.GetValueOrDefault(menuId) + item.Quantity;
        }

        return merged;
    }

    private async Task<OrderResponse> GetSingleAsync(int orderId, CancellationToken ct)
    {
        var responses = await BuildResponsesAsync(null, orderId, ct);
        return responses.First();
    }

    private async Task<IReadOnlyList<OrderResponse>> BuildResponsesAsync(int? userId, int? orderId, CancellationToken ct)
    {
        var records = await orders.LoadAsync(userId, orderId, ct);
        return records.Select(record => ToResponse(record.Order, record.Items, record.Payment, record.History)).ToList();
    }

    private static OrderResponse ToResponse(
        Order order, IReadOnlyList<OrderItem> items, Payment? payment, IReadOnlyList<OrderStatusHistory> history)
    {
        var subtotal = items.Sum(line => line.LineTotal);

        return new OrderResponse(
            PublicId.Order(order.Id),
            order.UserId,
            $"{order.User.FirstName} {order.User.LastName}".Trim(),
            items.Select(line => new OrderItemResponse(
                PublicId.Menu(line.MenuItemId), line.ItemName, line.Quantity, line.SnapshotPrice)).ToList(),
            items.Sum(line => line.Quantity),
            subtotal,
            Math.Max(0m, subtotal - order.TotalAmount),
            order.TotalAmount,
            null,
            payment?.PaymentMethod ?? DefaultPaymentMethod,
            order.Address?.AddressLine1 ?? string.Empty,
            order.Notes ?? string.Empty,
            order.User.MobileNumber ?? string.Empty,
            order.OrderStatus.Name,
            history.Select(entry => new OrderStatusHistoryResponse(
                string.IsNullOrEmpty(entry.FromStatus) ? null : entry.FromStatus,
                entry.ToStatus,
                entry.ChangedByUserId,
                ActorRoleFor(entry.Source),
                entry.Source,
                entry.ChangedAt)).ToList(),
            order.Archived,
            order.CreatedAt,
            order.UpdatedAt);
    }

    private static string ActorRoleFor(string source) =>
        source.StartsWith("admin", StringComparison.OrdinalIgnoreCase) ? AuthRoles.Admin
        : source.StartsWith("customer", StringComparison.OrdinalIgnoreCase) ? AuthRoles.Customer
        : "system";
}
