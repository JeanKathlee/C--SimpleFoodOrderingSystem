using FoodJS.Api.Services;
using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
var keyRingDirectory = string.IsNullOrWhiteSpace(keyRingPath)
    ? Path.Combine(builder.Environment.ContentRootPath, ".data-protection-keys")
    : Path.GetFullPath(keyRingPath, builder.Environment.ContentRootPath);
builder.Services.AddDataProtection()
    .SetApplicationName("FoodJS")
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingDirectory));
// secret key for login tokens, don't put real secrets in appsettings.json
builder.Configuration["Jwt:Secret"] ??= builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")
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
builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<IMenuRepository, MenuRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();
builder.Services.AddScoped<IUnitOfWorkRepository, UnitOfWorkRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICartRepository, CartRepository>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ICouponRepository, CouponRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAdminInsightsRepository, AdminInsightsRepository>();
builder.Services.AddScoped<IAdminInsightsService, AdminInsightsService>();
builder.Services.AddScoped<HealthService>();
builder.Services.AddScoped<OrderStatusSeeder>();
builder.Services.AddScoped<CouponSeeder>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ICouponService, CouponService>();

// checks the login token on every request
// to lock an endpoint add [Authorize], for admin only add [Authorize(Roles = AuthService.AdminRole)]
builder.Services.AddHttpClient();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UsersService>();
builder.Services.AddScoped<EmailVerificationService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) => await context.HttpContext.Response.WriteAsJsonAsync(
        new { message = "Too many requests. Please wait before trying again." }, ct);
    options.AddPolicy("email-verification", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(10), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("password-recovery", context => RateLimitPartition.GetFixedWindowLimiter(
        (context.Connection.RemoteIpAddress?.ToString() ?? "unknown") + context.Request.Path, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(15), QueueLimit = 0, AutoReplenishment = true }));
});
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
            OnTokenValidated = async context =>
            {
                var subject = context.Principal?.FindFirst("sub")?.Value;
                var role = context.Principal?.FindFirst("role")?.Value;
                var version = context.Principal?.FindFirst("auth_version")?.Value;
                if (!int.TryParse(subject, out var userId) ||
                    !int.TryParse(version, out var authVersion) ||
                    !await context.HttpContext.RequestServices.GetRequiredService<AuthService>()
                        .IsTokenAccountActiveAsync(userId, role, authVersion))
                    context.Fail("This account is unavailable.");
            },
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
    var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWorkRepository>();
    var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
    await unit.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<OrderStatusSeeder>().EnsureSeededAsync();
    await auth.EnsureRolesAsync();
    // demo accounts and the old node menu, only on your own machine
    if (app.Environment.IsDevelopment())
    {
        await auth.SeedDevelopmentDataAsync();
    }
    // Explicit opt-in for an isolated public demo: catalog only, never predictable
    // demo accounts. Normal Production startup does not seed sample data.
    if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Demo:SeedCatalog"))
    {
        await scope.ServiceProvider.GetRequiredService<IMenuService>().SeedDevelopmentDataAsync(app.Environment.ContentRootPath);
        await scope.ServiceProvider.GetRequiredService<CouponSeeder>().EnsureDevelopmentCouponsAsync();
    }

    if (!app.Environment.IsDevelopment())
    {
        await auth.SeedConfiguredAdminAsync(
            builder.Configuration["BootstrapAdmin:Email"],
            builder.Configuration["BootstrapAdmin:Password"]);
    }

}

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/auth") || context.Request.Path.StartsWithSegments("/api/users"))
        context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseCors("Frontend");
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
if (!app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");
}

app.Run();

public partial class Program;
