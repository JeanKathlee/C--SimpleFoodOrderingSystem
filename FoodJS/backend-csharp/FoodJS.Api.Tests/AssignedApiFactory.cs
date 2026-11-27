using System.Security.Claims;
using System.Text.Encodings.Web;
using FoodJS.Api.Data;
using FoodJS.Api.Services;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
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
using Microsoft.Extensions.Configuration;

namespace FoodJS.Api.Tests;

// Runs the real Program/controllers/services/migrations against an isolated database.
// Most tests use an isolated test scheme; security tests can opt into real JWT authentication.
internal sealed class AssignedApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly string? databaseFile;
    private readonly bool useTestAuthentication;
    private readonly bool useFakeEmail;
    public TestEmailService Emails { get; } = new();
    public TestClock Clock { get; } = new();
    public Dictionary<string, string?> Settings { get; } = new();
    public Action<IServiceCollection>? OverrideServices { get; set; }

    public AssignedApiFactory(bool fileDatabase = false, bool useTestAuthentication = true, bool useFakeEmail = true)
    {
        this.useTestAuthentication = useTestAuthentication;
        this.useFakeEmail = useFakeEmail;
        connection.Open();
        if (fileDatabase) databaseFile = Path.Combine(Path.GetTempPath(), $"foodjs-checkout-{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<FoodJsDbContext>();
            services.RemoveAll<DbContextOptions<FoodJsDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FoodJsDbContext>>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            if (useFakeEmail)
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(Emails);
            }
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
            OverrideServices?.Invoke(services);
        });
    }

    public Task<HttpResponseMessage> ConfirmEmailAsync(HttpClient client, string email)
    {
        var link = Emails.Messages.Last(message => message.Email == email).Link;
        var query = QueryHelpers.ParseQuery(link.Fragment.TrimStart('#'));
        return client.PostAsJsonAsync("/api/auth/verify-email", new { email, token = query["token"].ToString() });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await connection.DisposeAsync();
        if (databaseFile is not null)
            foreach (var suffix in new[] { "", "-shm", "-wal" }) File.Delete(databaseFile + suffix);
    }
}

internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}

internal sealed class TestEmailService : IEmailService
{
    public bool Configured { get; set; } = true;
    public bool FailDelivery { get; set; }
    public List<(string Email, Uri Link)> Messages { get; } = [];
    public List<(string Email, Uri Link)> PasswordResets { get; } = [];
    public List<string> PasswordChanges { get; } = [];
    public void EnsureConfigured()
    {
        if (!Configured) throw new FoodJS.Api.Common.ApiException(503, "Email delivery is not configured.");
    }
    public Task SendVerificationAsync(string recipient, Uri link, CancellationToken ct = default)
    {
        EnsureConfigured();
        if (FailDelivery) throw new FoodJS.Api.Common.ApiException(503, "SMTP unavailable.");
        Messages.Add((recipient, link));
        return Task.CompletedTask;
    }
    public Task SendPasswordResetAsync(string recipient, Uri link, CancellationToken ct = default)
    {
        EnsureConfigured();
        if (FailDelivery) throw new FoodJS.Api.Common.ApiException(503, "SMTP unavailable.");
        PasswordResets.Add((recipient, link));
        return Task.CompletedTask;
    }
    public Task SendPasswordChangedAsync(string recipient, CancellationToken ct = default)
    {
        EnsureConfigured();
        if (FailDelivery) throw new FoodJS.Api.Common.ApiException(503, "SMTP unavailable.");
        PasswordChanges.Add(recipient);
        return Task.CompletedTask;
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
