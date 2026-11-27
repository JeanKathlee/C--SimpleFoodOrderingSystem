using FoodJS.Api.Controllers;
using FoodJS.Api.Contracts;
using FoodJS.Api.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class RemainingFeatureFlowTests : IAsyncLifetime
{
    private readonly AssignedApiFactory factory = new();
    private HttpClient anonymous = null!, admin = null!, customer = null!, other = null!;
    private int customerId, otherId;

    public async Task InitializeAsync()
    {
        anonymous = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == "customer");
        var adminRole = await db.Roles.SingleAsync(r => r.Name == "admin");
        var first = new User { FirstName = "First", LastName = "QA", Email = "first@qa.invalid", PasswordHash = "", Role = role };
        var second = new User { FirstName = "Second", LastName = "QA", Email = "second@qa.invalid", PasswordHash = "", Role = role };
        var owner = new User { FirstName = "Admin", LastName = "QA", Email = "admin@qa.invalid", PasswordHash = "", Role = adminRole };
        db.AddRange(first, second, owner);
        await db.SaveChangesAsync();
        customerId = first.Id; otherId = second.Id;
        admin = Client(owner.Id, "admin"); customer = Client(first.Id, "customer"); other = Client(second.Id, "customer");
    }

    private HttpClient Client(int id, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", id.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact]
    public async Task Admin_profile_update_rejects_duplicate_email_and_customer_cannot_edit_other_accounts()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PatchAsJsonAsync($"/api/users/{otherId}", new { firstName = "Changed" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/users/{customerId}", new { email = "SECOND@QA.INVALID" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/users/{customerId}", new { firstName = "Updated", address = "QA Cebu" })).StatusCode);
        var profile = await customer.GetFromJsonAsync<JsonElement>("/api/users/me");
        Assert.Equal("Updated", profile.GetProperty("firstName").GetString());
        Assert.Equal("QA Cebu", profile.GetProperty("address").GetString());
    }

    [Fact]
    public async Task Coupon_crud_and_service_validation_keep_admin_permissions_and_unique_codes()
    {
        var input = new { code = "QA2026", discount = 10, discountType = "percentage", minOrderAmount = 100, maxUses = 1 };
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync("/api/coupons", input)).StatusCode);
        var created = await admin.PostAsJsonAsync("/api/coupons", input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/coupons", input)).StatusCode);
        var invalid = await anonymous.PostAsJsonAsync("/api/coupons/validate", new { code = "QA2026", orderAmount = 99 });
        Assert.False((await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valid").GetBoolean());
        var valid = await anonymous.PostAsJsonAsync("/api/coupons/validate", new { code = "QA2026", orderAmount = 200 });
        Assert.Equal(20m, (await valid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("discountAmount").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/coupons/{id}", new { code = "QA2026", discount = 25, discountType = "fixed", minOrderAmount = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/coupons/{id}")).StatusCode);
    }

    [Theory]
    [InlineData(101, "percentage", 0, -1)]
    [InlineData(10, "unknown", 0, -1)]
    [InlineData(10, "fixed", -1, -1)]
    [InlineData(10, "fixed", 0, -2)]
    public async Task Invalid_coupon_definitions_are_not_persisted(int discount, string discountType, int minOrderAmount, int maxUses)
    {
        var response = await admin.PostAsJsonAsync("/api/coupons", new { code = "QA2026", discount, discountType, minOrderAmount, maxUses });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<FoodJsDbContext>().Coupons.ToListAsync());
    }

    [Fact]
    public async Task Notifications_are_scoped_to_the_owner_and_read_updates_do_not_cross_accounts()
    {
        int id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            var row = new Notification { UserId = customerId, Title = "QA", Message = "QA update", Type = "Pending" };
            db.Notifications.Add(row); await db.SaveChangesAsync(); id = row.Id;
        }
        Assert.Empty((await other.GetFromJsonAsync<JsonElement[]>("/api/notifications"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PatchAsync($"/api/notifications/{id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await customer.PatchAsync($"/api/notifications/{id}/read", null)).StatusCode);
        Assert.True((await customer.GetFromJsonAsync<JsonElement[]>("/api/notifications"))![0].GetProperty("read").GetBoolean());
    }

    [Fact]
    public async Task Clear_soft_deletes_hidden_cart_rows_but_preserves_other_customers_bags()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            var item = new MenuItem { Name = "Hidden", Price = 100, PrepTime = 5, IsDeleted = true, Category = new Category { Name = "QA" } };
            db.CartItems.AddRange(
                new CartItem { MenuItem = item, ShoppingCart = new ShoppingCart { UserId = customerId }, Quantity = 1 },
                new CartItem { MenuItem = item, ShoppingCart = new ShoppingCart { UserId = otherId }, Quantity = 1 });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await customer.DeleteAsync("/api/cart")).StatusCode);
        using var check = factory.Services.CreateScope();
        var rows = await check.ServiceProvider.GetRequiredService<FoodJsDbContext>().CartItems.IgnoreQueryFilters().Include(i => i.ShoppingCart).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.True(Assert.Single(rows, r => r.ShoppingCart.UserId == customerId).IsDeleted);
        Assert.False(Assert.Single(rows, r => r.ShoppingCart.UserId == otherId).IsDeleted);
    }

    [Fact]
    public async Task Customer_cannot_read_admin_insights()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/admin-insights")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin-insights")).StatusCode);
    }

    public async Task DisposeAsync()
    {
        anonymous.Dispose(); admin.Dispose(); customer.Dispose(); other.Dispose();
        await factory.DisposeAsync();
    }
}
