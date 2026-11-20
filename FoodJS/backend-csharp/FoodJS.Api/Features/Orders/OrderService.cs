using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Features.Checkout;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace FoodJS.Api.Features.Orders;

public sealed class OrderService(FoodJsDbContext db, IDataProtectionProvider? protection = null) : IOrderService
{
    // Addresses.City is required by the schema, but checkout only collects one free-text
    // address (map picker / typed). The full text goes into AddressLine1.
    private const string UnspecifiedCity = "N/A";
    private const string DefaultPaymentMethod = "cash";
    private static readonly IDataProtectionProvider FallbackProtection = new EphemeralDataProtectionProvider();
    private readonly IDataProtector quoteProtector = (protection ?? FallbackProtection).CreateProtector("FoodJS.CheckoutQuote.v1");

    private sealed record PricedCheckout(List<(MenuItem Item, string? Choice, int Quantity, decimal UnitPrice, decimal LineTotal)> Lines,
        Coupon? Coupon, decimal Subtotal, decimal Discount, decimal Total);
    private sealed record QuoteProof(int UserId, string Fingerprint, DateTimeOffset ExpiresAt);
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static string Fingerprint(PricedCheckout price) => Hash(new {
        items = price.Lines.OrderBy(line => line.Item.Id).ThenBy(line => line.Choice, StringComparer.Ordinal).Select(line => new {
            line.Item.Id, line.Item.Name, line.Choice, line.Quantity, price = line.UnitPrice.ToString("G29", CultureInfo.InvariantCulture) }),
        coupon = price.Coupon?.Code, discount = price.Discount.ToString("G29", CultureInfo.InvariantCulture), total = price.Total.ToString("G29", CultureInfo.InvariantCulture)
    });
    private static string SubmissionHash(CreateOrderRequest request) => Hash(new {
        items = request.Items?.OrderBy(item => item.MenuItemId, StringComparer.Ordinal).ThenBy(item => item.SelectedChoice, StringComparer.Ordinal)
            .Select(item => new { item.MenuItemId, item.Quantity, choice = ItemChoiceRules.Normalize(item.SelectedChoice) }),
        name = request.CustomerName?.Trim(), mobile = NormalizeMobile(request.MobileNumber), address = request.Address?.Trim(),
        note = request.Note?.Trim() ?? "", payment = request.PaymentMethod?.Trim().ToLowerInvariant() ?? "cash", coupon = request.CouponCode?.Trim()
    });
    private static string NormalizeMobile(string? value) => System.Text.RegularExpressions.Regex.Replace(value ?? "", @"[\s-]", "");
    private static string? ParseSubmissionKey(CreateOrderRequest request)
    {
        if (string.IsNullOrEmpty(request.IdempotencyKey))
        {
            if (!string.IsNullOrWhiteSpace(request.QuoteToken)) throw new ApiException(400, "A submission key is required for a reviewed checkout.");
            return null;
        }
        if (!Guid.TryParse(request.IdempotencyKey, out var key)) throw new ApiException(400, "Invalid submission key.");
        return key.ToString("D");
    }
    private static void ValidateQuotedContact(CreateOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Address) || request.Address.Trim().Length > 2000)
            throw new ApiException(400, "Enter a delivery address of up to 2000 characters.");
        if ((request.CustomerName?.Trim().Length ?? 0) > 120 || (request.Note?.Length ?? 0) > 1000)
            throw new ApiException(400, "Use up to 120 characters for the recipient and 1000 for delivery notes.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(NormalizeMobile(request.MobileNumber), @"^(09\d{9}|\+639\d{9}|639\d{9})$"))
            throw new ApiException(400, "Enter a valid Philippine mobile number.");
        if (!string.Equals(request.PaymentMethod ?? "cash", "cash", StringComparison.OrdinalIgnoreCase))
            throw new ApiException(400, "Only cash on delivery is currently available.");
    }
    public async Task<CheckoutQuoteResponse> QuoteAsync(int userId, CreateOrderRequest request, CancellationToken ct = default)
    {
        var price = await CalculateAsync(request, ct);
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        var token = quoteProtector.Protect(JsonSerializer.Serialize(new QuoteProof(userId, Fingerprint(price), expires)));
        return new(token, expires, price.Lines.Select(line => new OrderItemResponse(PublicId.Menu(line.Item.Id), line.Item.Name,
            line.Quantity, line.UnitPrice, line.Choice)).ToList(), price.Lines.Sum(line => line.Quantity), price.Subtotal,
            price.Discount, price.Total, price.Coupon?.Code);
    }
    private void ValidateQuote(int userId, string token, PricedCheckout price)
    {
        QuoteProof? proof;
        try { proof = JsonSerializer.Deserialize<QuoteProof>(quoteProtector.Unprotect(token)); }
        catch (Exception failure) when (failure is CryptographicException or JsonException)
        { throw new ApiException(409, "This checkout review is no longer valid. Review the current total again."); }
        if (proof is null || proof.UserId != userId || proof.ExpiresAt <= DateTimeOffset.UtcNow || proof.Fingerprint != Fingerprint(price))
            throw new ApiException(409, "Menu prices or discounts changed, or your review expired. Review the current total again.");
    }

    public async Task<OrderResponse?> GetForAdminAsync(string publicId, CancellationToken ct = default)
    {
        if (!PublicId.TryParseOrder(publicId, out var id)) return null;
        return (await BuildResponsesAsync(db.Orders.Where(order => order.Id == id), ct)).FirstOrDefault();
    }

    public async Task<OrderPageResponse> GetPageAsync(int? userId, OrderPageRequest request, CancellationToken ct = default)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 50)
            throw new ApiException(400, "Page must be positive and pageSize must be between 1 and 50.");
        if (request.FromDate > request.ToDate || request.ToDate == DateOnly.MaxValue)
            throw new ApiException(400, "Choose a valid date range with From on or before To.");
        var status = string.IsNullOrWhiteSpace(request.Status) || request.Status == "All" ? null : request.Status;
        if (status is not null && !OrderStatusRules.IsKnown(status)) throw new ApiException(400, "Invalid order status.");
        var search = request.Search?.Trim().ToLowerInvariant() ?? "";
        // Dates in these screens are Philippine calendar dates. The upper bound is exclusive.
        static string Boundary(DateOnly date) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8))
            .ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var from = request.FromDate is { } first ? Boundary(first) : null;
        var to = request.ToDate is { } last ? Boundary(last.AddDays(1)) : null;
        // SQLite cannot translate DateTimeOffset ordering. Parameterized SQL uses julianday
        // to compare instants, then LIMIT/OFFSET before EF materializes the selected page.
        const string sql = """
            SELECT o.* FROM Orders o JOIN OrderStatuses s ON s.Id = o.OrderStatusId
            WHERE ({0} IS NULL OR o.UserId = {0}) AND ({1} IS NULL OR s.Name = {1})
              AND ({2} = '' OR instr(lower(o.CustomerName), {2}) > 0 OR instr(lower('O-' || o.Id), {2}) > 0)
              AND ({3} IS NULL OR julianday(o.CreatedAt) >= julianday({3}))
              AND ({4} IS NULL OR julianday(o.CreatedAt) < julianday({4}))
            """;
        object[] args = [userId!, status!, search, from!, to!];
        var count = await db.Orders.FromSql(FormattableStringFactory.Create(sql, args)).CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)request.PageSize));
        var page = Math.Min(request.Page, totalPages);
        object[] pageArgs = [.. args, request.PageSize, (page - 1) * request.PageSize];
        var orders = await db.Orders.FromSql(FormattableStringFactory.Create(
                sql + " ORDER BY julianday(o.CreatedAt) DESC, o.Id DESC LIMIT {5} OFFSET {6}", pageArgs))
            .AsNoTracking().Include(order => order.OrderStatus).ToListAsync(ct);
        var ids = orders.Select(order => order.Id).ToList();
        var quantities = await db.OrderItems.Where(item => ids.Contains(item.OrderId)).GroupBy(item => item.OrderId)
            .Select(group => new { Id = group.Key, Count = group.Sum(item => item.Quantity) }).ToDictionaryAsync(item => item.Id, item => item.Count, ct);
        var items = orders.OrderByDescending(order => order.CreatedAt).ThenByDescending(order => order.Id)
            .Select(order => new OrderListItemResponse(PublicId.Order(order.Id), order.CustomerName ?? "", order.OrderStatus.Name,
                quantities.GetValueOrDefault(order.Id), order.TotalAmount, order.CreatedAt)).ToList();
        return new(items, count, page, request.PageSize, totalPages);
    }

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

        if (request.Items.Any(item => item is null)) throw new ApiException(400, "Invalid order item.");
        var key = ParseSubmissionKey(request);
        var requestHash = key is null ? "" : SubmissionHash(request);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (key is not null)
        {
            var prior = await db.CheckoutSubmissions.AsNoTracking().SingleOrDefaultAsync(entry => entry.UserId == userId && entry.Key == key, ct);
            if (prior is not null)
            {
                if (prior.RequestHash != requestHash) throw new ApiException(409, "This submission key was already used for different checkout details.");
                return await GetForUserAsync(userId, PublicId.Order(prior.OrderId), ct)
                    ?? throw new ApiException(409, "This order was already submitted and later removed. Check your order history.");
            }
        }
        if (!string.IsNullOrWhiteSpace(request.QuoteToken)) ValidateQuotedContact(request);

        var user = await db.Users.FirstOrDefaultAsync(entry => entry.Id == userId, ct)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Unauthorized. User not found.");

        var priced = await CalculateAsync(request, ct);
        var (lines, coupon, subtotal, discount, total) = priced;
        if (!string.IsNullOrWhiteSpace(request.QuoteToken)) ValidateQuote(userId, request.QuoteToken, priced);

        // 3. Delivery address: reuse an identical saved address, otherwise save a new one.
        var address = await FindOrCreateAddressAsync(userId, request.Address, ct);

        var pendingStatus = await GetStatusRowAsync(OrderStatusRules.Pending, ct);
        var now = DateTimeOffset.UtcNow;
        var mobile = NormalizeMobile(request.MobileNumber);

        var order = new Order
        {
            UserId = userId,
            CustomerName = customerName,
            MobileNumber = mobile,
            CouponCode = coupon?.Code,
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
            SelectedChoice = line.Choice,
            Quantity = line.Quantity,
            SnapshotPrice = line.UnitPrice,
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
        if (key is not null) db.CheckoutSubmissions.Add(new CheckoutSubmission { UserId = userId, Key = key, RequestHash = requestHash, OrderId = order.Id });

        if (coupon is not null)
        {
            coupon.UsedCount += 1;
        }

        // Remember the contact number the first time a customer orders without one on file.
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

    private async Task<PricedCheckout> CalculateAsync(CreateOrderRequest request, CancellationToken ct)
    {
        if (request.Items is not { Count: > 0 }) throw new ApiException(400, "Items are required.");
        // 1. Price every line from the menu, never from the client.
        var quantities = MergeLines(request.Items);
        var menuIds = quantities.Keys.Select(key => key.MenuId).Distinct().ToList();
        var menuItems = await db.MenuItems
            .Where(item => menuIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, ct);

        var lines = new List<(MenuItem Item, string? Choice, int Quantity, decimal UnitPrice, decimal LineTotal)>();
        foreach (var ((menuId, choice), quantity) in quantities)
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

            var unitPrice = item.Price + ItemChoiceRules.GetAddOnPrice(choice);
            lines.Add((item, choice, quantity, unitPrice, Math.Round(unitPrice * quantity, 2)));
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

        return new(lines, coupon, subtotal, discount, total);
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

    // Only merge matching products AND choices; different upgrades have different prices.
    private static Dictionary<(int MenuId, string? Choice), int> MergeLines(IEnumerable<CreateOrderItem?> items)
    {
        var merged = new Dictionary<(int MenuId, string? Choice), int>();
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

            var choice = ItemChoiceRules.Normalize(item.SelectedChoice);
            _ = ItemChoiceRules.GetAddOnPrice(choice);
            var key = (menuId, choice);
            var currentQuantity = merged.GetValueOrDefault(key);
            if (item.Quantity > int.MaxValue - currentQuantity)
            {
                throw new ApiException(StatusCodes.Status400BadRequest, "Item quantity is too large.");
            }

            merged[key] = currentQuantity + item.Quantity;
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
            order.CustomerName ?? string.Empty,
            items.Select(line => new OrderItemResponse(
                PublicId.Menu(line.MenuItemId), line.ItemName, line.Quantity, line.SnapshotPrice,
                line.SelectedChoice)).ToList(),
            items.Sum(line => line.Quantity),
            subtotal,
            Math.Max(0m, subtotal - order.TotalAmount),
            order.TotalAmount,
            order.CouponCode,
            payment?.PaymentMethod ?? DefaultPaymentMethod,
            order.Address?.AddressLine1 ?? string.Empty,
            order.Notes ?? string.Empty,
            order.MobileNumber ?? string.Empty,
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
