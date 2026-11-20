using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Development;
using FoodJS.Api.Features.Menu;
using FoodJS.Api.Features.Orders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
var migrationBridge = builder.Configuration.GetValue<bool>("MigrationBridge:Enabled");
if (migrationBridge && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("The migration bridge is restricted to Development.");
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddDataProtection();
if (migrationBridge)
{
    var referenceUrl = new Uri(builder.Configuration["MigrationBridge:ReferenceApiUrl"] ?? "http://localhost:3001/");
    if (!referenceUrl.IsLoopback || referenceUrl.Scheme != "http" || referenceUrl.AbsolutePath != "/")
        throw new InvalidOperationException("MigrationBridge:ReferenceApiUrl must be an HTTP loopback origin.");
    builder.Services.AddHttpClient<MigrationDependencies>(client => { client.BaseAddress = referenceUrl; client.Timeout = TimeSpan.FromSeconds(10); })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    builder.Services.AddAuthentication(MigrationAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, MigrationAuthenticationHandler>(MigrationAuthenticationHandler.SchemeName, _ => { });
}

builder.Services.AddDbContext<FoodJsDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// The Node API accepted JSON bodies up to 50 MB (menu images are sent as base64).
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 50 * 1024 * 1024);

builder.Services
    .AddControllers(options => options.Filters.Add<ApiExceptionFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // Node returned { "message": "..." } for bad input; keep that shape instead of ProblemDetails.
        options.InvalidModelStateResponseFactory = context =>
        {
            var message = context.ModelState.Values
                .SelectMany(entry => entry.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Invalid request." : error.ErrorMessage)
                .FirstOrDefault() ?? "Invalid request.";
            return new BadRequestObjectResult(new { message });
        };
    });

builder.Services.AddProblemDetails();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var origins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? ["http://localhost:5173"];

        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Each feature registers its own service here.
builder.Services.AddScoped<IMenuService, EfMenuService>();
builder.Services.AddScoped<IOrderService, OrderService>();

var app = builder.Build();

// Apply migrations and make sure the order status lookup rows exist.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
    await db.Database.MigrateAsync();
    await OrderStatusSeeder.EnsureSeededAsync(db);
    if (migrationBridge)
        await scope.ServiceProvider.GetRequiredService<MigrationDependencies>().InitializeAsync();
}

app.UseExceptionHandler();
app.UseCors("Frontend");

// JWT authentication is registered by the auth feature (AddAuthentication().AddJwtBearer(...)).
// [Authorize] on the menu and order controllers needs it before it can authenticate requests.
app.UseAuthentication();
app.UseAuthorization();

if (migrationBridge)
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Method == "POST" && (context.Request.Path == "/api/orders" || context.Request.Path == "/api/orders/quote") && context.User.Identity?.IsAuthenticated == true)
            await context.RequestServices.GetRequiredService<MigrationDependencies>().RefreshCouponsAsync(context.RequestAborted);
        await next(context);
    });
    app.MapGet("/api/menu/dependencies/categories", async (MigrationDependencies dependencies, CancellationToken ct) =>
        Results.Ok(await dependencies.CategoriesAsync(ct)));
    app.Logger.LogWarning("Local migration bridge enabled: Menu/Orders use SQLite; identity and dependencies come from local Express. Do not deploy this profile.");
}

app.MapControllers();

app.Run();

public partial class Program;
