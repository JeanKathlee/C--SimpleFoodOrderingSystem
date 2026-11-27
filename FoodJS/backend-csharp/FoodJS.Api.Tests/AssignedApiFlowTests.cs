using FoodJS.Api.Controllers;
using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class AssignedApiFlowTests : IAsyncLifetime
{
    private readonly AssignedApiFactory factory = new();
    private HttpClient anonymous = null!;
    private HttpClient admin = null!;
    private HttpClient customer = null!;
    private HttpClient otherCustomer = null!;
    private int customerId;
    private int categoryId;
    private int menuId;

    public async Task InitializeAsync()
    {
        anonymous = factory.CreateClient(); // Executes real startup migrations and status seeding.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        var adminRole = new Role { Name = "admin" };
        var customerRole = new Role { Name = "customer" };
        var adminUser = User("Admin", adminRole);
        var user = User("Customer", customerRole);
        var other = User("Other", customerRole);
        var category = new Category { Name = "Burgers" };
        var menu = new MenuItem { Name = "Burger", Price = 100m, PrepTime = 10, Category = category };
        db.AddRange(adminUser, user, other, menu, new Coupon
        {
            Code = "SAVE10", Discount = 10m, DiscountType = "percentage",
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
        });
        await db.SaveChangesAsync();
        customerId = user.Id;
        categoryId = category.Id;
        menuId = menu.Id;
        admin = ClientFor(adminUser.Id, "admin");
        customer = ClientFor(user.Id, "customer");
        otherCustomer = ClientFor(other.Id, "customer");
    }

    public async Task DisposeAsync()
    {
        anonymous.Dispose();
        admin.Dispose();
        customer.Dispose();
        otherCustomer.Dispose();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task Menu_crud_preserves_description_and_image_and_supports_availability()
    {
        var created = await Read<MenuItemResponse>(await admin.PostAsJsonAsync("/api/menu", MenuRequest(
            description: "Original description", image: "data:image/png;base64,iVBORw0KGgo=")), HttpStatusCode.Created);
        Assert.Equal("Original description", created.Description);
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", created.Image);
        Assert.Equal("Burgers", created.Category);

        // Editing price without resending optional fields must not erase them.
        var updated = await Read<MenuItemResponse>(await admin.PutAsJsonAsync($"/api/menu/{created.Id}", MenuRequest(price: 125m)));
        Assert.Equal(125m, updated.Price);
        Assert.Equal(created.Description, updated.Description);
        Assert.Equal(created.Image, updated.Image);

        var hidden = await Read<MenuItemResponse>(await admin.PatchAsJsonAsync(
            $"/api/menu/{created.Id}/availability", new { isAvailable = false }));
        Assert.False(hidden.IsAvailable);
        var loaded = (await Read<List<MenuItemResponse>>(await admin.GetAsync("/api/menu")))
            .Single(item => item.Id == created.Id);
        Assert.Equal(created.Description, loaded.Description);
        Assert.Equal(created.Image, loaded.Image);
        Assert.False(loaded.IsAvailable);

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/menu/{created.Id}")).StatusCode);
        Assert.DoesNotContain(await Read<List<MenuItemResponse>>(await anonymous.GetAsync("/api/menu")), item => item.Id == created.Id);
    }

    [Theory]
    [InlineData(2 * 1024 * 1024 + 1, true)]
    [InlineData(5 * 1024 * 1024, true)]
    [InlineData(5 * 1024 * 1024 + 1, false)]
    public async Task Menu_create_and_update_enforce_the_five_mebibyte_upload_limit(int size, bool allowed)
    {
        var bytes = new byte[size];
        bytes[0] = 0x89; bytes[1] = 0x50; bytes[2] = 0x4E; bytes[3] = 0x47;
        var image = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
        using var created = await admin.PostAsJsonAsync("/api/menu", MenuRequest(image: image));
        using var updated = await admin.PutAsJsonAsync($"/api/menu/{PublicId.Menu(menuId)}", MenuRequest(image: image));
        Assert.Equal(allowed ? HttpStatusCode.Created : HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.BadRequest, updated.StatusCode);
        if (!allowed)
        {
            Assert.Contains("Image must be 5 MB or smaller.", await created.Content.ReadAsStringAsync());
            Assert.Contains("Image must be 5 MB or smaller.", await updated.Content.ReadAsStringAsync());
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        Assert.Equal(allowed ? image : null, (await db.MenuItems.SingleAsync(i => i.Id == menuId)).Image);
        Assert.Equal(allowed ? 2 : 1, await db.MenuItems.CountAsync());
    }

    [Fact]
    public async Task Menu_edit_without_availability_keeps_an_unavailable_item_hidden()
    {
        var id = PublicId.Menu(menuId);
        await Read<MenuItemResponse>(await admin.PatchAsJsonAsync($"/api/menu/{id}/availability", new { isAvailable = false }));
        var updated = await Read<MenuItemResponse>(await admin.PutAsJsonAsync($"/api/menu/{id}", MenuRequest(price: 120m)));
        Assert.False(updated.IsAvailable);
    }

    [Theory]
    [InlineData(0, "cat-1")]
    [InlineData(-1, "cat-1")]
    [InlineData(100, "cat-999999")]
    public async Task Invalid_menu_input_returns_a_message_and_does_not_create_an_item(int price, string category)
    {
        await Error(await admin.PostAsJsonAsync("/api/menu", new
        {
            name = "Invalid burger", categoryId = category, price, prepTime = 10
        }), HttpStatusCode.BadRequest);
        Assert.Single(await Read<List<MenuItemResponse>>(await anonymous.GetAsync("/api/menu")));
    }

    [Fact]
    public async Task Anonymous_and_customer_requests_cannot_use_admin_operations()
    {
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/menu")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/menu", MenuRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync("/api/menu", MenuRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PutAsJsonAsync($"/api/menu/{PublicId.Menu(menuId)}", MenuRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.DeleteAsync($"/api/menu/{PublicId.Menu(menuId)}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PatchAsJsonAsync(
            $"/api/menu/{PublicId.Menu(menuId)}/availability", new { isAvailable = false })).StatusCode);

        var order = await CreateOrder();
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/orders/admin/all")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PatchAsJsonAsync(
            $"/api/orders/{order.Id}/status", new { status = "Preparing" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.DeleteAsync($"/api/orders/{order.Id}")).StatusCode);
    }

    [Fact]
    public async Task Checkout_and_order_lifecycle_round_trip_real_http_contracts()
    {
        var response = await customer.PostAsJsonAsync("/api/orders", new
        {
            customerName = "Delivery Recipient", mobileNumber = "09990000000",
            address = "Pinned delivery address, Manila", note = "Call on arrival", paymentMethod = "cash",
            couponCode = "SAVE10", discount = 9999,
            items = new[] { new { menuItemId = PublicId.Menu(menuId), quantity = 2, selectedChoice = "Go Large Iced Tea", price = 0.01 } }
        });
        var order = await Read<OrderResponse>(response, HttpStatusCode.Created);
        Assert.Equal(300m, order.Subtotal);
        Assert.Equal(30m, order.Discount);
        Assert.Equal(270m, order.Total);
        Assert.Equal("Delivery Recipient", order.CustomerName);
        Assert.Equal("09990000000", order.MobileNumber);
        Assert.Equal("SAVE10", order.CouponCode);
        Assert.Equal("Pinned delivery address, Manila", order.Address);
        Assert.Equal("Call on arrival", order.Note);
        Assert.Equal("cash", order.PaymentMethod);
        Assert.Equal("Go Large Iced Tea", Assert.Single(order.Items).SelectedChoice);
        Assert.Equal(150m, Assert.Single(order.Items).Price);
        Assert.Equal("Pending", order.Status);
        Assert.Equal("customer-create", Assert.Single(order.StatusHistory).Source);
        Assert.Equal(order.Id, Assert.Single(await Read<List<OrderResponse>>(await customer.GetAsync("/api/orders"))).Id);
        Assert.Equal(order.Id, Assert.Single(await Read<List<OrderResponse>>(await admin.GetAsync("/api/orders/admin/all"))).Id);

        foreach (var status in new[] { "Preparing", "Ready", "Delivered" })
        {
            var changed = await Read<OrderResponse>(await admin.PatchAsJsonAsync(
                $"/api/orders/{order.Id}/status", new { status }));
            Assert.Equal(status, changed.Status);
        }
        // Retrying an already-applied status must not add duplicate history/notifications.
        await Read<OrderResponse>(await admin.PatchAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Delivered" }));
        var tracked = await Read<OrderResponse>(await customer.GetAsync($"/api/orders/{order.Id}"));
        Assert.Equal(new[] { "Pending", "Preparing", "Ready", "Delivered" }, tracked.StatusHistory.Select(entry => entry.ToStatus));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        Assert.Equal(270m, (await db.Payments.SingleAsync()).Amount);
        Assert.Equal(1, (await db.Coupons.SingleAsync()).UsedCount);
        Assert.Equal(4, await db.Notifications.CountAsync());
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Preparing")]
    public async Task Customer_can_cancel_only_their_eligible_order(string initialStatus)
    {
        var order = await CreateOrder();
        if (initialStatus == "Preparing")
            await Read<OrderResponse>(await admin.PatchAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Preparing" }));
        var cancelled = await Read<OrderResponse>(await customer.PatchAsync($"/api/orders/{order.Id}/cancel", null));
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("customer-cancel", cancelled.StatusHistory.Last().Source);
        await Error(await customer.PatchAsync($"/api/orders/{order.Id}/cancel", null), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("Ready")]
    [InlineData("Delivered")]
    public async Task Kitchen_completed_orders_cannot_be_cancelled(string finalStatus)
    {
        var order = await CreateOrder();
        foreach (var status in new[] { "Preparing", "Ready", "Delivered" })
        {
            await Read<OrderResponse>(await admin.PatchAsJsonAsync($"/api/orders/{order.Id}/status", new { status }));
            if (status == finalStatus) break;
        }
        await Error(await customer.PatchAsync($"/api/orders/{order.Id}/cancel", null), HttpStatusCode.BadRequest);
        Assert.Equal(finalStatus, (await Read<OrderResponse>(await customer.GetAsync($"/api/orders/{order.Id}"))).Status);
    }

    [Theory]
    [InlineData("Ready")]
    [InlineData("Shipped")]
    public async Task Invalid_status_changes_return_a_message_and_leave_history_unchanged(string status)
    {
        var order = await CreateOrder();
        await Error(await admin.PatchAsJsonAsync($"/api/orders/{order.Id}/status", new { status }), HttpStatusCode.BadRequest);
        var unchanged = await Read<OrderResponse>(await customer.GetAsync($"/api/orders/{order.Id}"));
        Assert.Equal("Pending", unchanged.Status);
        Assert.Single(unchanged.StatusHistory);
    }

    [Fact]
    public async Task Customers_cannot_read_or_cancel_another_customers_order()
    {
        var order = await CreateOrder();
        await Error(await otherCustomer.GetAsync($"/api/orders/{order.Id}"), HttpStatusCode.NotFound);
        await Error(await otherCustomer.PatchAsync($"/api/orders/{order.Id}/cancel", null), HttpStatusCode.NotFound);
        Assert.Empty(await Read<List<OrderResponse>>(await otherCustomer.GetAsync("/api/orders")));
    }

    [Fact]
    public async Task Admin_can_delete_an_order_without_breaking_foreign_keys()
    {
        var order = await CreateOrder();
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/orders/{order.Id}")).StatusCode);
        await Error(await customer.GetAsync($"/api/orders/{order.Id}"), HttpStatusCode.NotFound);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.OrderItems.ToListAsync());
        Assert.Empty(await db.Payments.ToListAsync());
        Assert.Empty(await db.OrderStatusHistories.ToListAsync());
        Assert.NotNull((await db.Notifications.SingleAsync()).OrderId);
        Assert.True((await db.Orders.IgnoreQueryFilters().SingleAsync()).IsDeleted);
        Assert.Single(await db.OrderItems.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Ordered_menu_items_can_be_hidden_without_losing_past_order_lines()
    {
        var order = await CreateOrder();
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/menu/{PublicId.Menu(menuId)}")).StatusCode);
        Assert.Single((await Read<OrderResponse>(await customer.GetAsync($"/api/orders/{order.Id}"))).Items);
    }

    [Fact]
    public async Task Checkout_rejects_unavailable_items_without_saving_order_data()
    {
        await Read<MenuItemResponse>(await admin.PatchAsJsonAsync($"/api/menu/{PublicId.Menu(menuId)}/availability", new { isAvailable = false }));
        await Error(await customer.PostAsJsonAsync("/api/orders", OrderRequest()), HttpStatusCode.BadRequest);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.Payments.ToListAsync());
        Assert.Empty(await db.Addresses.ToListAsync());
    }

    [Fact]
    public async Task Delivery_address_is_saved_once_when_reused()
    {
        await CreateOrder();
        await CreateOrder();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        var address = await db.Addresses.SingleAsync();
        Assert.Equal(customerId, address.UserId);
        Assert.Equal("Delivery address, Manila", address.AddressLine1);
        Assert.All(await db.Orders.ToListAsync(), order => Assert.Equal(address.Id, order.AddressId));
    }

    [Fact]
    public async Task Checkout_missing_items_returns_the_expected_error_shape()
    {
        await Error(await customer.PostAsJsonAsync("/api/orders", new { customerName = "Recipient", items = Array.Empty<object>() }),
            HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Menu_paging_is_bounded_stable_and_keeps_the_existing_array_contract()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            db.MenuItems.AddRange(Enumerable.Range(0, 24).Select(index => new MenuItem
            {
                Name = $"Paged item {index:D2}", Price = 199.50m, PrepTime = 10, CategoryId = categoryId
            }));
            await db.SaveChangesAsync();
        }
        var first = await Read<MenuPageResponse>(await anonymous.GetAsync("/api/menu/paged?page=1&pageSize=10"));
        var second = await Read<MenuPageResponse>(await anonymous.GetAsync("/api/menu/paged?page=2&pageSize=10"));
        var last = await Read<MenuPageResponse>(await anonymous.GetAsync("/api/menu/paged?page=3&pageSize=10"));
        Assert.Equal(25, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(10, first.Items.Count);
        Assert.Equal(10, second.Items.Count);
        Assert.Equal(5, last.Items.Count);
        var ids = first.Items.Concat(second.Items).Concat(last.Items).Select(item => item.Id).ToList();
        Assert.Equal(25, ids.Distinct().Count());
        Assert.Equal(first.Items.Select(item => item.Id),
            (await Read<MenuPageResponse>(await anonymous.GetAsync("/api/menu/paged?page=1&pageSize=10"))).Items.Select(item => item.Id));
        Assert.Equal(25, (await Read<List<MenuItemResponse>>(await anonymous.GetAsync("/api/menu"))).Count);
    }

    [Fact]
    public async Task Menu_page_filters_search_description_category_and_availability_together()
    {
        int sidesId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            var sides = new Category { Name = "Sides" };
            db.MenuItems.AddRange(
                new MenuItem { Name = "Salad", Description = "VEGAN dressing", Price = 80m, PrepTime = 5, Category = sides, IsAvailable = false },
                new MenuItem { Name = "Other salad", Description = "Vegan dressing", Price = 85m, PrepTime = 5, Category = sides, IsAvailable = true },
                new MenuItem { Name = "Vegan burger", Price = 100m, PrepTime = 10, CategoryId = categoryId, IsAvailable = false });
            await db.SaveChangesAsync();
            sidesId = sides.Id;
        }
        var result = await Read<MenuPageResponse>(await admin.GetAsync(
            $"/api/menu/paged?search=%20vEgAn%20&categoryId={PublicId.Category(sidesId)}&isAvailable=false&pageSize=10"));
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Salad", Assert.Single(result.Items).Name);
        Assert.False(Assert.Single(result.Items).IsAvailable);
    }

    [Fact]
    public async Task Menu_paging_clamps_after_the_only_item_on_the_last_page_is_deleted()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            db.MenuItems.AddRange(Enumerable.Range(0, 10).Select(index => new MenuItem
            {
                Name = $"Last page {index:D2}", Price = 50m, PrepTime = 5, CategoryId = categoryId
            }));
            await db.SaveChangesAsync();
        }
        var before = await Read<MenuPageResponse>(await anonymous.GetAsync("/api/menu/paged?page=2&pageSize=10"));
        var last = Assert.Single(before.Items);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/menu/{last.Id}")).StatusCode);
        var after = await Read<MenuPageResponse>(await anonymous.GetAsync("/api/menu/paged?page=2&pageSize=10"));
        Assert.Equal(1, after.Page);
        Assert.Equal(10, after.Items.Count);
        Assert.Equal(1, after.TotalPages);
    }

    [Fact]
    public async Task Empty_menu_search_has_consistent_pagination_metadata()
    {
        var page = await Read<MenuPageResponse>(await anonymous.GetAsync("/api/menu/paged?search=does-not-exist&page=99"));
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.TotalPages);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=51")]
    [InlineData("categoryId=invalid")]
    public async Task Menu_paging_rejects_invalid_bounds_and_ids(string query) =>
        await Error(await anonymous.GetAsync($"/api/menu/paged?{query}"), HttpStatusCode.BadRequest);

    [Fact]
    public async Task Order_pages_are_bounded_newest_first_and_customer_scoped()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            var pending = await db.OrderStatuses.SingleAsync(status => status.Name == "Pending");
            var other = await db.Users.SingleAsync(user => user.FirstName == "Other");
            foreach (var index in Enumerable.Range(0, 24))
            {
                var order = new Order { UserId = customerId, CustomerName = "Page recipient", OrderStatusId = pending.Id,
                    TotalAmount = 598.50m, CreatedAt = DateTimeOffset.Parse("2026-11-20T10:00:00Z").AddMinutes(index) };
                db.OrderItems.Add(new OrderItem { Order = order, MenuItemId = menuId, ItemName = "Burger", Quantity = 3, SnapshotPrice = 199.50m, LineTotal = 598.50m });
            }
            db.Orders.Add(new Order { UserId = other.Id, CustomerName = "Private other order", OrderStatusId = pending.Id });
            await db.SaveChangesAsync();
        }
        var first = await Read<OrderPageResponse>(await customer.GetAsync("/api/orders/paged"));
        var second = await Read<OrderPageResponse>(await customer.GetAsync("/api/orders/paged?page=2"));
        var last = await Read<OrderPageResponse>(await customer.GetAsync("/api/orders/paged?page=3"));
        Assert.Equal(24, first.TotalCount); Assert.Equal(3, first.TotalPages);
        Assert.Equal(10, first.Items.Count); Assert.Equal(10, second.Items.Count); Assert.Equal(4, last.Items.Count);
        Assert.Equal(24, first.Items.Concat(second.Items).Concat(last.Items).Select(order => order.Id).Distinct().Count());
        Assert.All(first.Items, order => { Assert.Equal(3, order.ItemCount); Assert.Equal(598.50m, order.Total); });
        Assert.True(first.Items[0].CreatedAt > second.Items[0].CreatedAt);
        Assert.Single((await Read<OrderPageResponse>(await otherCustomer.GetAsync("/api/orders/paged"))).Items);
        Assert.Equal(25, (await Read<OrderPageResponse>(await admin.GetAsync("/api/orders/admin/paged"))).TotalCount);
        Assert.Equal(24, (await Read<List<OrderResponse>>(await customer.GetAsync("/api/orders"))).Count);
    }

    [Fact]
    public async Task Order_page_dates_use_Philippine_midnight_and_exclude_the_next_day()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            var pending = await db.OrderStatuses.SingleAsync(status => status.Name == "Pending");
            foreach (var instant in new[] { "2026-11-20T15:59:59Z", "2026-11-20T16:00:00Z", "2026-11-21T23:59:59.999+08:00", "2026-11-21T16:00:00Z" })
                db.Orders.Add(new Order { UserId = customerId, CustomerName = "Date recipient", OrderStatusId = pending.Id, CreatedAt = DateTimeOffset.Parse(instant) });
            await db.SaveChangesAsync();
        }
        var result = await Read<OrderPageResponse>(await admin.GetAsync("/api/orders/admin/paged?fromDate=2026-11-21&toDate=2026-11-21&status=Pending&search=dAtE"));
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, order => Assert.Equal(21, order.CreatedAt.ToOffset(TimeSpan.FromHours(8)).Day));
    }

    [Fact]
    public async Task Admin_details_include_saved_contacts_items_and_history_without_opening_customer_access()
    {
        var order = await CreateOrder();
        var details = await Read<OrderResponse>(await admin.GetAsync($"/api/orders/admin/{order.Id}"));
        Assert.Equal(order.CustomerName, details.CustomerName); Assert.Equal(order.MobileNumber, details.MobileNumber);
        Assert.Equal(order.Address, details.Address); Assert.Equal(order.Items, details.Items); Assert.Single(details.StatusHistory);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync($"/api/orders/admin/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/orders/admin/paged")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/orders/admin/paged")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherCustomer.GetAsync($"/api/orders/{order.Id}")).StatusCode);
        await Error(await admin.GetAsync("/api/orders/admin/O-999999"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Order_page_clamps_after_deletion_and_empty_search_has_consistent_metadata()
    {
        for (var index = 0; index < 11; index++) await CreateOrder();
        var last = await Read<OrderPageResponse>(await admin.GetAsync("/api/orders/admin/paged?page=2"));
        await admin.DeleteAsync($"/api/orders/{Assert.Single(last.Items).Id}");
        var clamped = await Read<OrderPageResponse>(await admin.GetAsync("/api/orders/admin/paged?page=2"));
        Assert.Equal(1, clamped.Page); Assert.Equal(10, clamped.Items.Count);
        var empty = await Read<OrderPageResponse>(await customer.GetAsync("/api/orders/paged?search=no-such-order&page=99"));
        Assert.Empty(empty.Items); Assert.Equal(0, empty.TotalCount); Assert.Equal(1, empty.Page);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=51")]
    [InlineData("pageSize=0")]
    [InlineData("status=Unknown")]
    [InlineData("fromDate=2026-11-22&toDate=2026-11-21")]
    [InlineData("fromDate=invalid")]
    public async Task Invalid_order_page_queries_return_a_message(string query) =>
        await Error(await admin.GetAsync($"/api/orders/admin/paged?{query}"), HttpStatusCode.BadRequest);

    private async Task<CheckoutQuoteResponse> CheckoutQuote(string? coupon = null) => await Read<CheckoutQuoteResponse>(
        await customer.PostAsJsonAsync("/api/orders/quote", new { items = new[] { new { menuItemId = PublicId.Menu(menuId), quantity = 2, selectedChoice = "Go Large Iced Tea", price = 0.01 } }, couponCode = coupon }));

    private CreateOrderRequest ReviewedCheckout(CheckoutQuoteResponse quote, string key, string name = "Recipient", string? address = "QA delivery, Manila", string mobile = "0917 123 4567", string payment = "cash") => new()
    {
        Items = [new() { MenuItemId = PublicId.Menu(menuId), Quantity = 2, SelectedChoice = "Go Large Iced Tea" }],
        CustomerName = name, Address = address, MobileNumber = mobile, PaymentMethod = payment, Note = "Call on arrival",
        CouponCode = quote.CouponCode, QuoteToken = quote.QuoteToken, IdempotencyKey = key
    };

    [Fact]
    public async Task Checkout_quote_uses_current_prices_choices_and_coupon_rules_without_writing_orders()
    {
        var quote = await CheckoutQuote("SAVE10");
        Assert.Equal(300m, quote.Subtotal); Assert.Equal(30m, quote.Discount); Assert.Equal(270m, quote.Total);
        Assert.Equal(2, quote.ItemCount); Assert.Equal(150m, Assert.Single(quote.Items).Price);
        Assert.Equal("Go Large Iced Tea", Assert.Single(quote.Items).SelectedChoice);
        Assert.True(quote.ExpiresAt > DateTimeOffset.UtcNow);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        Assert.Empty(await db.Orders.ToListAsync()); Assert.Equal(0, (await db.Coupons.SingleAsync()).UsedCount);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/orders/quote", OrderRequest())).StatusCode);
    }

    [Fact]
    public async Task Checkout_retries_return_one_order_and_do_not_repeat_side_effects()
    {
        var quote = await CheckoutQuote("SAVE10"); var request = ReviewedCheckout(quote, Guid.NewGuid().ToString());
        var first = await Read<OrderResponse>(await customer.PostAsJsonAsync("/api/orders", request), HttpStatusCode.Created);
        var again = await Read<OrderResponse>(await customer.PostAsJsonAsync("/api/orders", request), HttpStatusCode.Created);
        Assert.Equal(first.Id, again.Id); Assert.Equal("09171234567", again.MobileNumber);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        Assert.Equal(1, await db.Orders.CountAsync()); Assert.Equal(1, await db.Payments.CountAsync());
        Assert.Equal(1, await db.OrderStatusHistories.CountAsync()); Assert.Equal(1, await db.Notifications.CountAsync());
        Assert.Equal(1, await db.CheckoutSubmissions.CountAsync()); Assert.Equal(1, (await db.Coupons.SingleAsync()).UsedCount);
    }

    [Fact]
    public async Task Changed_prices_require_another_review_before_any_order_is_written()
    {
        var quote = await CheckoutQuote();
        using (var scope = factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>(); (await db.MenuItems.SingleAsync()).Price = 200m; await db.SaveChangesAsync(); }
        await Error(await customer.PostAsJsonAsync("/api/orders", ReviewedCheckout(quote, Guid.NewGuid().ToString())), HttpStatusCode.Conflict);
        using (var scope = factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>(); Assert.Equal(0, await db.Orders.CountAsync()); Assert.Equal(0, await db.CheckoutSubmissions.CountAsync()); }
        var updated = await CheckoutQuote(); Assert.Equal(500m, updated.Total);
        var saved = await Read<OrderResponse>(await customer.PostAsJsonAsync("/api/orders", ReviewedCheckout(updated, Guid.NewGuid().ToString())), HttpStatusCode.Created);
        Assert.Equal(updated.Total, saved.Total);
    }

    [Fact]
    public async Task Reviewed_checkout_cannot_use_another_users_quote_or_a_tampered_quote()
    {
        var quote = await CheckoutQuote();
        await Error(await otherCustomer.PostAsJsonAsync("/api/orders", ReviewedCheckout(quote, Guid.NewGuid().ToString())), HttpStatusCode.Conflict);
        await Error(await customer.PostAsJsonAsync("/api/orders", ReviewedCheckout(quote with { QuoteToken = "tampered" }, Guid.NewGuid().ToString())), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_submission_key_cannot_be_reused_for_changed_details_or_recreate_a_deleted_order()
    {
        var quote = await CheckoutQuote(); var key = Guid.NewGuid().ToString(); var request = ReviewedCheckout(quote, key);
        var saved = await Read<OrderResponse>(await customer.PostAsJsonAsync("/api/orders", request), HttpStatusCode.Created);
        await Error(await customer.PostAsJsonAsync("/api/orders", ReviewedCheckout(quote, key, name: "Changed name")), HttpStatusCode.Conflict);
        await admin.DeleteAsync($"/api/orders/{saved.Id}");
        await Error(await customer.PostAsJsonAsync("/api/orders", request), HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope(); Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<FoodJsDbContext>().Orders.CountAsync());
    }

    [Fact]
    public async Task Accepted_submission_replays_even_after_menu_or_coupon_changes()
    {
        var quote = await CheckoutQuote("SAVE10"); var request = ReviewedCheckout(quote, Guid.NewGuid().ToString());
        var saved = await Read<OrderResponse>(await customer.PostAsJsonAsync("/api/orders", request), HttpStatusCode.Created);
        using (var scope = factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>(); (await db.MenuItems.SingleAsync()).IsAvailable = false; (await db.Coupons.SingleAsync()).IsActive = false; await db.SaveChangesAsync(); }
        var replay = await Read<OrderResponse>(await customer.PostAsJsonAsync("/api/orders", request), HttpStatusCode.Created);
        Assert.Equal(saved.Id, replay.Id); Assert.Equal(saved.Total, replay.Total);
    }

    [Theory]
    [InlineData("   ", "09171234567", "cash", "Recipient")]
    [InlineData("Address", "12345", "cash", "Recipient")]
    [InlineData("Address", "09171234567", "card", "Recipient")]
    [InlineData("Address", "09171234567", "unknown", "Recipient")]
    [InlineData("Address", "09171234567", "cash", "   ")]
    public async Task Reviewed_checkout_validates_delivery_contact_and_supported_payment(string address, string mobile, string payment, string name)
    {
        var quote = await CheckoutQuote();
        await Error(await customer.PostAsJsonAsync("/api/orders", ReviewedCheckout(quote, Guid.NewGuid().ToString(), name, address, mobile, payment)), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Expired_server_reviews_are_rejected_before_creation()
    {
        var quote = await CheckoutQuote();
        var provider = factory.Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
        var protector = provider.CreateProtector("FoodJS.CheckoutQuote.v1");
        var proof = System.Text.Json.Nodes.JsonNode.Parse(Microsoft.AspNetCore.DataProtection.DataProtectionCommonExtensions.Unprotect(protector, quote.QuoteToken))!;
        proof["ExpiresAt"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
        var expiredToken = Microsoft.AspNetCore.DataProtection.DataProtectionCommonExtensions.Protect(protector, proof.ToJsonString());
        await Error(await customer.PostAsJsonAsync("/api/orders", ReviewedCheckout(quote with { QuoteToken = expiredToken }, Guid.NewGuid().ToString())), HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task Reviewed_checkout_requires_a_valid_submission_key(string key)
    {
        var quote = await CheckoutQuote();
        await Error(await customer.PostAsJsonAsync("/api/orders", ReviewedCheckout(quote, key)), HttpStatusCode.BadRequest);
    }

    private HttpClient ClientFor(int id, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", id.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static User User(string name, Role role) => new()
    {
        FirstName = name, LastName = "Test", Email = $"{name}@example.invalid", PasswordHash = "unused", Role = role
    };

    private object MenuRequest(decimal price = 100m, string? description = null, string? image = null) => new
    {
        name = "New burger", categoryId = PublicId.Category(categoryId), price, prepTime = 10, description, image
    };

    private object OrderRequest() => new
    {
        customerName = "Recipient", mobileNumber = "09990000000", address = "Delivery address, Manila",
        items = new[] { new { menuItemId = PublicId.Menu(menuId), quantity = 1 } }
    };

    private async Task<OrderResponse> CreateOrder() =>
        await Read<OrderResponse>(await customer.PostAsJsonAsync("/api/orders", OrderRequest()), HttpStatusCode.Created);

    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        using (response)
        {
            var json = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {json}");
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
    }

    private static async Task Error(HttpResponseMessage response, HttpStatusCode status)
    {
        var error = await Read<JsonElement>(response, status);
        Assert.Equal(JsonValueKind.String, error.GetProperty("message").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }
}
