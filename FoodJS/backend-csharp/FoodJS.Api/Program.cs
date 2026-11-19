using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Features.Auth;
using FoodJS.Api.Features.Coupons;
using FoodJS.Api.Features.Menu;
using FoodJS.Api.Features.Orders;
using FoodJS.Api.Features.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// secret key for login tokens, don't put real secrets in appsettings.json
builder.Configuration["Jwt:Secret"] ??= builder.Environment.IsDevelopment()
    ? "dev-only-jwt-secret-change-me-0123456789"
    : throw new InvalidOperationException("Jwt:Secret is not configured.");

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
builder.Services.AddScoped<IMenuService, EfMenuService>(); // menu add, edit, availability, delete
builder.Services.AddScoped<MenuService>();                  // menu list, search, filter, details
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ICouponService, CouponService>();

// checks the login token on every request
// to lock an endpoint add [Authorize], for admin only add [Authorize(Roles = AuthService.AdminRole)]
builder.Services.AddHttpClient();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UsersService>();
builder.Services.AddAuthorization();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new()
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            IssuerSigningKey = AuthService.SigningKey(builder.Configuration),
            NameClaimType = "email",
            RoleClaimType = "role",
        };
        // same error messages as the old node api
        options.Events = new()
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return context.Response.WriteAsJsonAsync(new
                {
                    message = context.AuthenticateFailure is null
                        ? "Unauthorized. Token required."
                        : "Unauthorized. Invalid token."
                });
            },
            OnForbidden = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return context.Response.WriteAsJsonAsync(new { message = "Forbidden. Admin access required." });
            },
        };
    });

var app = builder.Build();

// Apply migrations and make sure the order status lookup rows exist.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
    await db.Database.MigrateAsync();
    await OrderStatusSeeder.EnsureSeededAsync(db);

    // demo accounts and the old node menu, only on your own machine
    if (app.Environment.IsDevelopment())
    {
        await AuthService.SeedDevelopmentDataAsync(db);
        await MenuService.SeedDevelopmentDataAsync(db, app.Environment.ContentRootPath);
    }
}

app.UseExceptionHandler();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
