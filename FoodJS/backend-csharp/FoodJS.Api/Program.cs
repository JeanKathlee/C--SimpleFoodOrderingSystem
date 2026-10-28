using FoodJS.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using FoodJS.Api.Features.Menu;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors("Frontend");
app.MapControllers();

app.Run();

public partial class Program;
