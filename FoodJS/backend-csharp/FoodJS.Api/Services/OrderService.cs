using FoodJS.Api.Contracts;
using FoodJS.Api.Common;
using FoodJS.Api.Repositories;
using FoodJS.Api.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace FoodJS.Api.Services;

public sealed class OrderService(IOrderRepository orders, IMenuRepository menu, IReferenceDataRepository referenceData, IUnitOfWorkRepository unitOfWork, IDataProtectionProvider? protection = null) : IOrderService
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
        return (await BuildResponsesAsync(null, id, ct)).FirstOrDefault();
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
        var query = new OrderQuery(userId, status, search, from, to);
        var count = await orders.CountAsync(query, ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)request.PageSize));
        var page = Math.Min(request.Page, totalPages);
        var records = await orders.PageAsync(query, request.PageSize, (page - 1) * request.PageSize, ct);
        var items = records.Select(record => new OrderListItemResponse(PublicId.Order(record.Order.Id),
            record.Order.CustomerName ?? "", record.Order.OrderStatus.Name, record.Items.Sum(i => i.Quantity),
            record.Order.TotalAmount, record.Order.CreatedAt)).ToList();
        return new(items, count, page, request.PageSize, totalPages);
    }

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

        var orders = await BuildResponsesAsync(userId, id, ct);
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
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        if (key is not null)
        {
            var prior = await orders.FindSubmissionAsync(userId, key, ct);
            if (prior is not null)
            {
                if (prior.RequestHash != requestHash) throw new ApiException(409, "This submission key was already used for different checkout details.");
                return await GetForUserAsync(userId, PublicId.Order(prior.OrderId), ct)
                    ?? throw new ApiException(409, "This order was already submitted and later removed. Check your order history.");
            }
        }
        if (!string.IsNullOrWhiteSpace(request.QuoteToken)) ValidateQuotedContact(request);

        var user = await referenceData.FindUserAsync(userId, ct)
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
        orders.Add(order);

        orders.AddItems(lines.Select(line => new OrderItem
        {
            Order = order,
            MenuItemId = line.Item.Id,
            ItemName = line.Item.Name,
            SelectedChoice = line.Choice,
            Quantity = line.Quantity,
            SnapshotPrice = line.UnitPrice,
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
        if (key is not null) orders.AddSubmission(new CheckoutSubmission { UserId = userId, Key = key, RequestHash = requestHash, OrderId = order.Id });

        if (coupon is not null)
        {
            coupon.UsedCount += 1;
        }

        // Remember the contact number the first time a customer orders without one on file.
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
        if (order is null)
        {
            return false;
        }

        order.IsDeleted = true;
        await unitOfWork.SaveAsync(ct);
        return true;
    }

    private async Task<PricedCheckout> CalculateAsync(CreateOrderRequest request, CancellationToken ct)
    {
        if (request.Items is not { Count: > 0 }) throw new ApiException(400, "Items are required.");
        // 1. Price every line from the menu, never from the client.
        var quantities = MergeLines(request.Items);
        var menuIds = quantities.Keys.Select(key => key.MenuId).Distinct().ToList();
        var menuItems = (await menu.FindManyAsync(menuIds, ct)).ToDictionary(item => item.Id);

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
            coupon = await referenceData.FindCouponAsync(couponCode, ct);
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
        var orders = await BuildResponsesAsync(null, orderId, ct);
        return orders.First();
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
