using FoodJS.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using FoodJS.Api.Features.Auth;
using FoodJS.Api.Features.Menu;
using FoodJS.Api.Features.Users;

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

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
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

// Each feature registers its own service here. Replace the in-memory example
// with an Entity Framework Core implementation when persistence is added.
builder.Services.AddSingleton<IMenuService, InMemoryMenuService>();

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

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await AuthService.SeedDevelopmentDataAsync(scope.ServiceProvider.GetRequiredService<FoodJsDbContext>());
}

app.UseExceptionHandler();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
