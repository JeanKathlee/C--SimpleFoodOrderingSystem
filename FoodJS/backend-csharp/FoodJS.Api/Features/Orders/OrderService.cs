using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Features.Checkout;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Features.Orders;

public sealed class OrderService(FoodJsDbContext db) : IOrderService
{
    // Addresses.City is required by the schema, but checkout only collects one free-text
    // address (map picker / typed). The full text goes into AddressLine1.
    private const string UnspecifiedCity = "N/A";
    private const string DefaultPaymentMethod = "cash";

    // ---- Order Management: List / View / Track --------------------------------------

    public async Task<IReadOnlyList<OrderResponse>> ListForUserAsync(int userId, CancellationToken ct = default) =>
        await BuildResponsesAsync(db.Orders.Where(order => order.UserId == userId), ct);

    public async Task<IReadOnlyList<OrderResponse>> ListAllAsync(CancellationToken ct = default) =>
        await BuildResponsesAsync(db.Orders, ct);

    public async Task<OrderResponse?> GetForUserAsync(int userId, string publicId, CancellationToken ct = default)
    {
        if (!PublicId.TryParseOrder(publicId, out var id))
        {
            return null;
        }

        var orders = await BuildResponsesAsync(
            db.Orders.Where(order => order.Id == id && order.UserId == userId), ct);
        return orders.FirstOrDefault();
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

        var user = await db.Users.FirstOrDefaultAsync(entry => entry.Id == userId, ct)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Unauthorized. User not found.");

        // 1. Price every line from the menu, never from the client.
        var quantities = MergeLines(request.Items);
        var menuIds = quantities.Keys.ToList();
        var menuItems = await db.MenuItems
            .Where(item => menuIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, ct);

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
            coupon = await db.Coupons.FirstOrDefaultAsync(entry => entry.Code == couponCode, ct);
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

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

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
        db.Orders.Add(order);

        db.OrderItems.AddRange(lines.Select(line => new OrderItem
        {
            Order = order,
            MenuItemId = line.Item.Id,
            ItemName = line.Item.Name,
            Quantity = line.Quantity,
            SnapshotPrice = line.Item.Price,
            LineTotal = line.LineTotal
        }));

        db.Payments.Add(new Payment
        {
            Order = order,
            PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod)
                ? DefaultPaymentMethod
                : request.PaymentMethod.Trim(),
            Amount = total,
            PaymentStatus = "Pending"
        });

        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Order = order,
            FromStatus = string.Empty,
            ToStatus = OrderStatusRules.Pending,
            ChangedByUserId = userId,
            Source = "customer-create",
            ChangedAt = now
        });

        // Save once so the order has its generated id for the notification text.
        await db.SaveChangesAsync(ct);

        db.Notifications.Add(NewStatusNotification(userId, order.Id, OrderStatusRules.Pending, now));

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

        await db.SaveChangesAsync(ct);
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

        var order = await db.Orders
            .Include(entry => entry.OrderStatus)
            .FirstOrDefaultAsync(entry => entry.Id == id && entry.UserId == userId, ct);
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

        var order = await db.Orders
            .Include(entry => entry.OrderStatus)
            .FirstOrDefaultAsync(entry => entry.Id == id, ct);
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

        var order = await db.Orders.FirstOrDefaultAsync(entry => entry.Id == id, ct);
        if (order is null)
        {
            return false;
        }

        // Notifications.OrderId has no cascade; detach them so the customer keeps their history.
        var notifications = await db.Notifications.Where(entry => entry.OrderId == id).ToListAsync(ct);
        foreach (var notification in notifications)
        {
            notification.Order = null;
            notification.OrderId = null;
        }

        // Order items, payment and status history are removed by their Cascade foreign keys.
        db.Orders.Remove(order);
        await db.SaveChangesAsync(ct);
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

        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            FromStatus = from,
            ToStatus = to,
            // ChangedByUserId is a foreign key; skip it when the token's user is not in the table.
            ChangedByUserId = await db.Users.AnyAsync(user => user.Id == actorId, ct) ? actorId : null,
            Source = source,
            ChangedAt = now
        });

        db.Notifications.Add(NewStatusNotification(order.UserId, order.Id, to, now));
        await db.SaveChangesAsync(ct);
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
        await db.OrderStatuses.FirstOrDefaultAsync(status => status.Name == name, ct)
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
        var existing = await db.Addresses
            .FirstOrDefaultAsync(entry => entry.UserId == userId && entry.AddressLine1.ToLower() == lowered, ct);
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
        db.Addresses.Add(created);
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
        var orders = await BuildResponsesAsync(db.Orders.Where(order => order.Id == orderId), ct);
        return orders.First();
    }

    // The Order entity has no collection navigations, so children are loaded in four
    // batched queries and stitched together. Sorting happens in memory because the
    // SQLite provider cannot ORDER BY a DateTimeOffset column.
    private async Task<IReadOnlyList<OrderResponse>> BuildResponsesAsync(
        IQueryable<Order> query, CancellationToken ct)
    {
        var orders = await query
            .AsNoTracking()
            .Include(order => order.User)
            .Include(order => order.OrderStatus)
            .Include(order => order.Address)
            .ToListAsync(ct);

        if (orders.Count == 0)
        {
            return [];
        }

        var ids = orders.Select(order => order.Id).ToList();
        var items = await db.OrderItems.AsNoTracking().Where(line => ids.Contains(line.OrderId)).ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking().Where(payment => ids.Contains(payment.OrderId)).ToListAsync(ct);
        var history = await db.OrderStatusHistories.AsNoTracking()
            .Where(entry => ids.Contains(entry.OrderId)).ToListAsync(ct);

        var itemsByOrder = items.ToLookup(line => line.OrderId);
        var paymentByOrder = payments.GroupBy(payment => payment.OrderId)
            .ToDictionary(group => group.Key, group => group.First());
        var historyByOrder = history.ToLookup(entry => entry.OrderId);

        return orders
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Select(order => ToResponse(
                order,
                itemsByOrder[order.Id].OrderBy(line => line.Id).ToList(),
                paymentByOrder.GetValueOrDefault(order.Id),
                historyByOrder[order.Id].OrderBy(entry => entry.ChangedAt).ThenBy(entry => entry.Id).ToList()))
            .ToList();
    }

    private static OrderResponse ToResponse(
        Order order, List<OrderItem> items, Payment? payment, List<OrderStatusHistory> history)
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
