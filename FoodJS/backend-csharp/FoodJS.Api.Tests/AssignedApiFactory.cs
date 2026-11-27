using System.Security.Claims;
using System.Text.Encodings.Web;
using FoodJS.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodJS.Api.Tests;

// Runs the real Program/controllers/services/migrations against an isolated database.
// Most tests use an isolated test scheme; security tests can opt into real JWT authentication.
internal sealed class AssignedApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly string? databaseFile;
    private readonly bool useTestAuthentication;

    public AssignedApiFactory(bool fileDatabase = false, bool useTestAuthentication = true)
    {
        this.useTestAuthentication = useTestAuthentication;
        connection.Open();
        if (fileDatabase) databaseFile = Path.Combine(Path.GetTempPath(), $"foodjs-checkout-{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<FoodJsDbContext>();
            services.RemoveAll<DbContextOptions<FoodJsDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FoodJsDbContext>>();
            services.AddDbContext<FoodJsDbContext>(options => {
                if (databaseFile is null) options.UseSqlite(connection);
                else options.UseSqlite($"Data Source={databaseFile};Default Timeout=10;Pooling=False");
            });
            if (useTestAuthentication)
            {
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName, _ => { });
                services.AddAuthorization();
            }
            // Keep checkout quote protection isolated from the developer machine's
            // persisted key ring. Tests must be repeatable and must not require
            // access to the current Windows user's profile.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await connection.DisposeAsync();
        if (databaseFile is not null)
            foreach (var suffix in new[] { "", "-shm", "-wal" }) File.Delete(databaseFile + suffix);
    }
}

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AssignedFeatureTests";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
        [
            new Claim("sub", userId.ToString()),
            new Claim(ClaimTypes.Role, Request.Headers["X-Test-Role"].ToString())
        ], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
