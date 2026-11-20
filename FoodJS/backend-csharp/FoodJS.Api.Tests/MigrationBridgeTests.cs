using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodJS.Api.Data;
using FoodJS.Api.Development;
using FoodJS.Api.Features.Menu;
using FoodJS.Api.Features.Orders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class MigrationBridgeTests
{
    [Fact]
    public async Task Real_bridge_relays_identity_maps_categories_and_saves_menu_and_checkout_in_sqlite()
    {
        await using var factory = new BridgeFactory();
        using var anonymous = factory.CreateClient();
        var menu = await anonymous.GetFromJsonAsync<MenuPageResponse>("/api/menu/paged");
        Assert.Equal("M-1001", Assert.Single(menu!.Items).Id);
        var categories = await anonymous.GetFromJsonAsync<JsonElement[]>("/api/menu/dependencies/categories");
        var categoryId = Assert.Single(categories!).GetProperty("id").GetString();
        Assert.Equal(menu.Items[0].CategoryId, categoryId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/orders")).StatusCode);
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new("Bearer", "admin-session");
        var created = await admin.PostAsJsonAsync("/api/menu", new { name = "C# only item", categoryId, price = 99.50, prepTime = 10 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.Content.ReadFromJsonAsync<MenuItemResponse>();
        using var customer = factory.CreateClient();
        customer.DefaultRequestHeaders.Authorization = new("Bearer", "customer-session");
        var forbidden = await customer.DeleteAsync($"/api/menu/{saved!.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var denial = await forbidden.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Administrator access", denial.GetProperty("message").GetString());
        var submitted = await customer.PostAsJsonAsync("/api/orders", new {
            customerName = "Checkout snapshot", mobileNumber = "09171234567", address = "Local test delivery",
            items = new[] { new { menuItemId = saved.Id, quantity = 2, price = 1 } }, couponCode = "SAVE10"
        });
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var order = await submitted.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal(179.10m, order!.Total);
        Assert.Equal("Checkout snapshot", order.CustomerName);
        Assert.Single((await admin.GetFromJsonAsync<List<OrderResponse>>("/api/orders/admin/all"))!);
        Assert.All(factory.Reference.Requests, request => Assert.Equal("GET", request.Method));
        Assert.Contains(factory.Reference.Requests, request => request.Authorization == "Bearer admin-session");
    }

    [Fact]
    public async Task Dependency_refresh_does_not_restore_deleted_menu_or_erase_local_coupon_usage()
    {
        await using var factory = new BridgeFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "admin-session");
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/menu/M-1001")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        var coupon = await db.Coupons.SingleAsync(); coupon.UsedCount = 3;
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<MigrationDependencies>().InitializeAsync();
        Assert.Empty(await db.MenuItems.ToListAsync());
        Assert.Equal(3, (await db.Coupons.SingleAsync()).UsedCount);
    }

    [Fact]
    public async Task Rejected_sessions_and_unavailable_reference_api_do_not_authenticate()
    {
        await using var factory = new BridgeFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "rejected-session");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders")).StatusCode);
        factory.Reference.Unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task Bridge_configuration_is_rejected_outside_development()
    {
        await using var factory = new BridgeFactory("Production");
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("restricted to Development", exception.Message);
    }
}

internal sealed class BridgeFactory(string environment = "Development") : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    public ReferenceStub Reference { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();
        builder.UseEnvironment(environment);
        builder.UseSetting("MigrationBridge:Enabled", "true");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<FoodJsDbContext>();
            services.RemoveAll<DbContextOptions<FoodJsDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FoodJsDbContext>>();
            services.AddDbContext<FoodJsDbContext>(options => options.UseSqlite(connection));
            services.AddHttpClient<MigrationDependencies>().ConfigurePrimaryHttpMessageHandler(() => Reference);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync(); await connection.DisposeAsync();
    }
}

internal sealed class ReferenceStub : HttpMessageHandler
{
    public bool Unavailable { get; set; }
    public List<(string Method, string? Authorization)> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add((request.Method.Method, request.Headers.Authorization?.ToString()));
        if (Unavailable) throw new HttpRequestException("Reference API stopped");
        object? body = request.RequestUri!.AbsolutePath switch
        {
            "/api/categories" => new[] { new { id = "cat-burgers", name = "Burgers", description = "Test dependency" } },
            "/api/menu" => new[] { new { id = "M-1001", name = "Reference burger", categoryId = "cat-burgers", price = 100, prepTime = 10 } },
            "/api/coupons" => new[] { new { code = "SAVE10", discount = 10, discountType = "percentage", expiryDate = "2099-12-31", maxUses = -1 } },
            "/api/users/me" when request.Headers.Authorization?.Parameter == "admin-session" => new { id = 1, email = "admin@local.test", role = "admin", firstName = "Admin", lastName = "Test" },
            "/api/users/me" when request.Headers.Authorization?.Parameter == "customer-session" => new { id = 2, email = "customer@local.test", role = "customer", firstName = "Customer", lastName = "Test" },
            _ => null
        };
        return Task.FromResult(new HttpResponseMessage(body is null ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
        { Content = JsonContent.Create(body) });
    }
}
