using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodJS.Api.Contracts;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using FoodJS.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class SoftDeleteApiTests : IAsyncLifetime
{
    private readonly AssignedApiFactory factory = new(useTestAuthentication: false);
    private readonly List<HttpClient> clients = [];
    private HttpClient anonymous = null!, admin = null!, customer = null!, other = null!;
    private AuthResponse customerSession = null!, otherSession = null!;
    private MenuItemResponse menu = null!;
    private CategoryResponse category = null!;
    private const string Password = "Qa-password-123!";

    public async Task InitializeAsync()
    {
        anonymous = NewClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            var user = new User { FirstName = "Admin", LastName = "QA", Email = "admin@soft.invalid",
                PasswordHash = "", IsEmailVerified = true, Role = await db.Roles.SingleAsync(r => r.Name == "admin") };
            user.PasswordHash = AuthService.Hasher.HashPassword(user, Password);
            db.Users.Add(user); await db.SaveChangesAsync();
        }
        var login = await anonymous.PostAsJsonAsync("/api/users/login", new { email = "admin@soft.invalid", password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        admin = NewClient((await login.Content.ReadFromJsonAsync<AuthResponse>())!.Token);
        customerSession = await Register("customer@soft.invalid");
        otherSession = await Register("other@soft.invalid");
        customer = NewClient(customerSession.Token); other = NewClient(otherSession.Token);
        var categoryResult = await admin.PostAsJsonAsync("/api/categories", new { name = "Soft category", description = "QA" });
        category = (await categoryResult.Content.ReadFromJsonAsync<CategoryResponse>())!;
        var itemResult = await admin.PostAsJsonAsync("/api/menu", new { name = "Soft meal", categoryId = category.Id, price = 100, prepTime = 5, drinkOptions = TestMealDrinks.Options });
        menu = (await itemResult.Content.ReadFromJsonAsync<MenuItemResponse>())!;
    }

    private HttpClient NewClient(string? token = null)
    {
        var client = factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        clients.Add(client); return client;
    }

    private async Task<AuthResponse> Register(string email)
    {
        var response = await anonymous.PostAsJsonAsync("/api/users/register", new
        { email, password = Password, firstName = "Soft", lastName = "Customer", address = "Original delivery, Cebu" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ConfirmEmailAsync(anonymous, email)).StatusCode);
        var login = await anonymous.PostAsJsonAsync("/api/users/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task Add(HttpClient client, string choice = "Go Regular Iced Tea")
    {
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/cart/items",
            new { menuItemId = menu.Id, quantity = 1, selectedChoice = choice })).StatusCode);
    }

    private async Task<OrderResponse> Order(string? coupon = null, string? key = null)
    {
        var response = await customer.PostAsJsonAsync("/api/orders", new
        {
            customerName = "Saved recipient", mobileNumber = "09171234567", address = "Original delivery, Cebu",
            items = new[] { new { menuItemId = menu.Id, quantity = 1, selectedChoice = "Go Regular Iced Tea" } }, couponCode = coupon, idempotencyKey = key
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    [Fact]
    public async Task Cart_remove_and_clear_preserve_rows_and_readding_does_not_resurrect_tombstones()
    {
        await Add(customer);
        var first = Assert.Single((await customer.GetFromJsonAsync<List<CartItemResponse>>("/api/cart"))!);
        Assert.Equal(HttpStatusCode.OK, (await customer.DeleteAsync($"/api/cart/items/{first.Id}")).StatusCode);
        Assert.Empty((await customer.GetFromJsonAsync<List<CartItemResponse>>("/api/cart"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.DeleteAsync($"/api/cart/items/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.PatchAsJsonAsync($"/api/cart/items/{first.Id}", new { quantity = 2 })).StatusCode);
        await Add(customer); await Add(customer, "Go Large Iced Tea"); await Add(other);
        Assert.NotEqual(first.Id, (await customer.GetFromJsonAsync<List<CartItemResponse>>("/api/cart"))![0].Id);
        Assert.Equal(HttpStatusCode.OK, (await customer.DeleteAsync("/api/cart")).StatusCode);
        Assert.Single((await other.GetFromJsonAsync<List<CartItemResponse>>("/api/cart"))!);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        var rows = await db.CartItems.IgnoreQueryFilters().Include(i => i.ShoppingCart).ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.All(rows.Where(i => i.ShoppingCart.UserId == customerSession.User.Id), i => Assert.True(i.IsDeleted));
        Assert.False(Assert.Single(rows, i => i.ShoppingCart.UserId == otherSession.User.Id).IsDeleted);
    }

    [Fact]
    public async Task Empty_category_delete_retains_the_row_and_rejects_later_updates_or_menu_assignment()
    {
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/categories/{category.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/menu/{menu.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/categories/{category.Id}")).StatusCode);
        Assert.Empty((await anonymous.GetFromJsonAsync<List<CategoryResponse>>("/api/categories"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/categories/{category.Id}", new { name = "Changed", description = "QA" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/menu", new
        { name = "Invalid", categoryId = category.Id, price = 100, prepTime = 5 })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        Assert.True((await db.Categories.IgnoreQueryFilters().SingleAsync()).IsDeleted);
        Assert.Single(await db.MenuItems.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Coupon_delete_retains_usage_and_saved_order_discount_but_disables_redemption_and_reuse()
    {
        var input = new { code = "SOFT10", discount = 10, discountType = "percentage", maxUses = 5 };
        var created = await admin.PostAsJsonAsync("/api/coupons", input);
        var coupon = (await created.Content.ReadFromJsonAsync<CouponResponse>())!;
        var order = await Order("SOFT10");
        Assert.Equal(90m, order.Total);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/coupons/{coupon.Id}")).StatusCode);
        Assert.Empty((await anonymous.GetFromJsonAsync<List<CouponResponse>>("/api/coupons"))!);
        var validate = await anonymous.PostAsJsonAsync("/api/coupons/validate", new { code = "SOFT10", orderAmount = 100 });
        Assert.False((await validate.Content.ReadFromJsonAsync<CouponValidationResponse>())!.Valid);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/coupons", input)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/coupons/{coupon.Id}", input)).StatusCode);
        var saved = (await customer.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!;
        Assert.Equal("SOFT10", saved.CouponCode); Assert.Equal(90m, saved.Total);
        using var scope = factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<FoodJsDbContext>().Coupons.IgnoreQueryFilters().SingleAsync();
        Assert.True(row.IsDeleted); Assert.Equal(1, row.UsedCount);
    }

    [Fact]
    public async Task Deleted_account_cannot_use_existing_JWT_login_or_reset_tokens_but_admin_keeps_its_orders()
    {
        var order = await Order();
        var forgot = await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { email = customerSession.User.Email });
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        var token = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
            factory.Emails.PasswordResets.Last().Link.Fragment.TrimStart('#'))["token"].ToString();
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/users/{customerSession.User.Id}")).StatusCode);
        foreach (var path in new[] { "/api/users/me", "/api/cart", "/api/orders", "/api/notifications" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await customer.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/users/login",
            new { email = customerSession.User.Email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/auth/reset-password",
            new { email = customerSession.User.Email, token, newPassword = "new-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/users/register",
            new { email = customerSession.User.Email, password = Password, firstName = "New", lastName = "Owner" })).StatusCode);
        var users = await admin.GetFromJsonAsync<List<AuthUserResponse>>("/api/users");
        Assert.DoesNotContain(users!, u => u.Id == customerSession.User.Id);
        var saved = (await admin.GetFromJsonAsync<OrderResponse>($"/api/orders/admin/{order.Id}"))!;
        Assert.Equal("Saved recipient", saved.CustomerName); Assert.Equal(100m, saved.Total);
        Assert.Equal("Original delivery, Cebu", saved.Address);
        Assert.Single((await admin.GetFromJsonAsync<List<OrderResponse>>("/api/orders/admin/all"))!);
        Assert.Equal(1, (await admin.GetFromJsonAsync<OrderPageResponse>("/api/orders/admin/paged"))!.TotalCount);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        Assert.True((await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == customerSession.User.Id)).IsDeleted);
        Assert.Single(await db.Orders.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.PasswordResetTokens.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Clearing_a_profile_address_soft_deletes_it_without_erasing_order_delivery_history()
    {
        var order = await Order();
        Assert.Equal(HttpStatusCode.OK, (await customer.PatchAsJsonAsync("/api/users/me", new { address = "" })).StatusCode);
        Assert.Equal("", (await customer.GetFromJsonAsync<AuthUserResponse>("/api/users/me"))!.Address);
        Assert.Equal("Original delivery, Cebu", (await customer.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!.Address);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        var oldAddress = await db.Addresses.IgnoreQueryFilters().SingleAsync(a => a.UserId == customerSession.User.Id);
        Assert.True(oldAddress.IsDeleted); Assert.True(oldAddress.IsDefault);
        Assert.Equal(HttpStatusCode.OK, (await customer.PatchAsJsonAsync("/api/users/me", new { address = "New address" })).StatusCode);
        Assert.Equal("Original delivery, Cebu", (await customer.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!.Address);
    }

    [Fact]
    public async Task Deleted_parent_category_hides_menu_and_cart_but_preserves_existing_order_snapshots()
    {
        var order = await Order();
        await Add(customer);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            (await db.Categories.SingleAsync()).IsDeleted = true;
            await db.SaveChangesAsync();
        }
        Assert.Empty((await anonymous.GetFromJsonAsync<List<MenuItemResponse>>("/api/menu"))!);
        Assert.Empty((await customer.GetFromJsonAsync<List<CartItemResponse>>("/api/cart"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/orders",
            new { customerName = "QA", items = new[] { new { menuItemId = menu.Id, quantity = 1 } } })).StatusCode);
        Assert.Equal("Soft meal", Assert.Single((await customer.GetFromJsonAsync<OrderResponse>($"/api/orders/{order.Id}"))!.Items).Name);
    }

    [Fact]
    public async Task Deleted_coupons_are_not_recreated_by_development_seeding()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<CouponSeeder>();
        await seeder.EnsureDevelopmentCouponsAsync();
        var rows = await db.Coupons.ToListAsync();
        Assert.Equal(3, rows.Count);
        foreach (var row in rows) row.IsDeleted = true;
        await db.SaveChangesAsync();
        await seeder.EnsureDevelopmentCouponsAsync();
        Assert.Empty(await db.Coupons.ToListAsync());
        Assert.Equal(3, await db.Coupons.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Deleted_submission_ledgers_still_prevent_duplicate_orders()
    {
        var key = Guid.NewGuid().ToString();
        var order = await Order(key: key);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            (await db.CheckoutSubmissions.SingleAsync()).IsDeleted = true;
            await db.SaveChangesAsync();
        }
        Assert.Equal(order.Id, (await Order(key: key)).Id);
        using var check = factory.Services.CreateScope();
        Assert.Single(await check.ServiceProvider.GetRequiredService<FoodJsDbContext>().Orders.ToListAsync());
    }

    public async Task DisposeAsync()
    {
        foreach (var client in clients) client.Dispose();
        await factory.DisposeAsync();
    }
}
