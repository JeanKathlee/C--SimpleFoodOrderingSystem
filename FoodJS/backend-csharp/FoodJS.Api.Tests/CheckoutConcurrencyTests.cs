using System.Net;
using System.Net.Http.Json;
using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Features.Orders;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class CheckoutConcurrencyTests
{
    [Fact]
    public async Task Concurrent_retries_on_separate_database_connections_commit_one_complete_order()
    {
        await using var factory = new AssignedApiFactory(fileDatabase: true);
        using var client = factory.CreateClient();
        int userId, menuId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            var user = new User { FirstName = "QA", LastName = "Recipient", Email = "qa@example.invalid", PasswordHash = "unused", Role = new Role { Name = "customer" } };
            var menu = new MenuItem { Name = "QA Burger", Price = 199.50m, PrepTime = 10, Category = new Category { Name = "Burgers" } };
            db.AddRange(user, menu, new Coupon { Code = "SAVE10", Discount = 10, DiscountType = "percentage", ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2) });
            await db.SaveChangesAsync(); userId = user.Id; menuId = menu.Id;
        }
        client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "customer");
        var items = new[] { new { menuItemId = PublicId.Menu(menuId), quantity = 3 } };
        var quoted = await client.PostAsJsonAsync("/api/orders/quote", new { items, couponCode = "SAVE10" });
        quoted.EnsureSuccessStatusCode(); var quote = (await quoted.Content.ReadFromJsonAsync<CheckoutQuoteResponse>())!;
        var request = new { items, quoteToken = quote.QuoteToken, idempotencyKey = Guid.NewGuid().ToString(), customerName = "QA Recipient", address = "QA address, Manila", mobileNumber = "09171234567", paymentMethod = "cash", couponCode = "SAVE10" };
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/api/orders", request), client.PostAsJsonAsync("/api/orders", request));
        foreach (var response in responses) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var first = (await responses[0].Content.ReadFromJsonAsync<OrderResponse>())!;
        var second = (await responses[1].Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal(first.Id, second.Id); Assert.Equal(quote.Total, first.Total);
        using var verify = factory.Services.CreateScope(); var store = verify.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        Assert.Equal(1, await store.Orders.CountAsync()); Assert.Equal(1, await store.CheckoutSubmissions.CountAsync());
        Assert.Equal(1, await store.Payments.CountAsync()); Assert.Equal(1, await store.Notifications.CountAsync());
        Assert.Equal(1, await store.OrderStatusHistories.CountAsync()); Assert.Equal(1, (await store.Coupons.SingleAsync()).UsedCount);
    }
}
