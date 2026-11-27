using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FoodJS.Api.Contracts;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using FoodJS.Api.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class EmailVerificationApiTests : IAsyncLifetime
{
    private readonly AssignedApiFactory factory = new(useTestAuthentication: false);
    private HttpClient client = null!;
    private const string Email = "verify@example.test";
    private const string Password = "Initial-password-123!";

    public Task InitializeAsync() { client = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); }
    private Task<HttpResponseMessage> Register(string email = Email) => client.PostAsJsonAsync("/api/users/register",
        new { email, password = Password, firstName = "Verify", lastName = "Customer" });
    private Task<HttpResponseMessage> Login(string email = Email) => client.PostAsJsonAsync("/api/users/login", new { email, password = Password });
    private Task<HttpResponseMessage> Resend(string email = Email) => client.PostAsJsonAsync("/api/auth/resend-verification", new { email });
    private string Token(string email = Email) => QueryHelpers.ParseQuery(factory.Emails.Messages.Last(m => m.Email == email).Link.Fragment.TrimStart('#'))["token"].ToString();
    private Task<HttpResponseMessage> Verify(string token, string email = Email) => client.PostAsJsonAsync("/api/auth/verify-email", new { email, token });
    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Signup_requires_email_verification_and_never_returns_or_stores_a_raw_token()
    {
        var result = await Register();
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        var body = await Json(result);
        Assert.True(body.GetProperty("verificationRequired").GetBoolean());
        Assert.True(body.GetProperty("emailSent").GetBoolean());
        Assert.False(body.TryGetProperty("token", out _));
        Assert.False(body.TryGetProperty("verificationToken", out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await Login()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        var token = Token();
        var link = Assert.Single(factory.Emails.Messages).Link;
        Assert.Equal("/verify-email", link.AbsolutePath);
        Assert.Equal("", link.Query);
        Assert.Contains("token=", link.Fragment);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            var record = await db.EmailVerificationTokens.SingleAsync();
            Assert.NotEqual(token, record.TokenHash);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), record.TokenHash);
            Assert.Equal(TimeSpan.FromMinutes(30), record.ExpiresAt - record.CreatedAt);
            Assert.False((await db.Users.SingleAsync()).IsEmailVerified);
        }
        var verified = await Verify(token);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.False((await Json(verified)).TryGetProperty("token", out _));
        var login = await Login();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.True(session.User.IsEmailVerified);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(token)).StatusCode);
    }

    [Fact]
    public async Task Expired_tampered_and_wrong_email_links_cannot_activate_an_account()
    {
        await Register(); var token = Token();
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(new string('A', 64))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(token, "other@example.test")).StatusCode);
        factory.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Login()).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public async Task Invalid_token_shapes_are_rejected(string? token)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/verify-email", new { email = Email, token })).StatusCode);
    }

    [Fact]
    public async Task Resend_has_a_cooldown_rotates_old_tokens_and_uses_generic_responses()
    {
        await Register(); var first = Token();
        var pending = await Json(await Resend());
        var missing = await Json(await Resend("missing@example.test"));
        Assert.Equal(pending.GetProperty("message").GetString(), missing.GetProperty("message").GetString());
        Assert.Single(factory.Emails.Messages);
        factory.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.OK, (await Resend()).StatusCode);
        Assert.Equal(2, factory.Emails.Messages.Count);
        var replacement = Token(); Assert.NotEqual(first, replacement);
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Verify(replacement)).StatusCode);
        var alreadyVerified = await Json(await Resend());
        Assert.Equal(pending.GetProperty("message").GetString(), alreadyVerified.GetProperty("message").GetString());
        Assert.Equal(2, factory.Emails.Messages.Count);
    }

    [Fact]
    public async Task Missing_email_configuration_fails_before_creating_an_account()
    {
        factory.Emails.Configured = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Register()).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.EmailVerificationTokens.ToListAsync());
    }

    [Fact]
    public async Task Delivery_failure_keeps_account_pending_and_resend_can_recover()
    {
        factory.Emails.FailDelivery = true;
        var result = await Register();
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        Assert.False((await Json(result)).GetProperty("emailSent").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await Login()).StatusCode);
        factory.Emails.FailDelivery = false;
        factory.Clock.Advance(TimeSpan.FromSeconds(61));
        await Resend();
        Assert.Equal(HttpStatusCode.OK, (await Verify(Token())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login()).StatusCode);
    }

    [Fact]
    public async Task Deleted_accounts_and_deleted_tokens_cannot_be_verified_or_resent()
    {
        await Register(); var token = Token();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            (await db.Users.SingleAsync()).IsDeleted = true;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(token)).StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(61)); await Resend();
        Assert.Single(factory.Emails.Messages);
        using var secondScope = factory.Services.CreateScope();
        var context = secondScope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        var deleted = await context.Users.IgnoreQueryFilters().SingleAsync();
        Assert.False(deleted.IsEmailVerified);
        deleted.IsDeleted = false;
        (await context.EmailVerificationTokens.IgnoreQueryFilters().SingleAsync()).IsDeleted = true;
        await context.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(token)).StatusCode);
    }

    [Fact]
    public async Task Password_recovery_does_not_bypass_verification()
    {
        await Register();
        var forgot = await Json(await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = Email }));
        Assert.False(forgot.TryGetProperty("resetToken", out _));
        Assert.Empty(factory.Emails.PasswordResets);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password",
            new { email = Email, token = new string('A', 64), newPassword = "replacement-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Login()).StatusCode);
    }

    [Fact]
    public async Task Email_changes_revoke_existing_sessions_and_require_a_new_bound_token()
    {
        await Register(); await Verify(Token());
        var session = (await (await Login()).Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        var changed = await client.PatchAsJsonAsync("/api/users/me", new { email = "new@example.test" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.False((await changed.Content.ReadFromJsonAsync<AuthUserResponse>())!.IsEmailVerified);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Login("new@example.test")).StatusCode);
        var token = Token("new@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Verify(token, "new@example.test")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login("new@example.test")).StatusCode);
    }

    [Fact]
    public async Task Verification_endpoints_rate_limit_abuse()
    {
        HttpResponseMessage? response = null;
        for (var i = 0; i < 31; i++) response = await Resend("missing@example.test");
        Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
    }

    [Fact]
    public async Task Tokens_bound_to_an_old_email_cannot_verify_a_changed_address()
    {
        await Register(); var oldToken = Token();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            (await db.Users.SingleAsync()).Email = "changed@example.test";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(oldToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(oldToken, "changed@example.test")).StatusCode);
    }

    [Theory]
    [InlineData("http://untrusted.example.test")]
    [InlineData("https://example.test/redirect?url=evil")]
    [InlineData("https://user:password@example.test")]
    public async Task Untrusted_frontend_configuration_is_rejected_before_signup(string url)
    {
        factory.Services.GetRequiredService<IConfiguration>()["Email:FrontendUrl"] = url;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Register()).StatusCode);
        Assert.Empty(factory.Emails.Messages);
    }

    [Fact]
    public async Task Migration_retains_administrators_but_does_not_silently_verify_existing_customers()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
        db.Users.AddRange(
            new User { FirstName = "Existing", LastName = "Admin", Email = "admin@example.test", PasswordHash = "unused",
                Role = await db.Roles.SingleAsync(r => r.Name == "admin"), IsEmailVerified = true },
            new User { FirstName = "Existing", LastName = "Customer", Email = "customer@example.test", PasswordHash = "unused",
                Role = await db.Roles.SingleAsync(r => r.Name == "customer"), IsEmailVerified = true });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261127091139_AddMenuDrinkOptions");
        await migrator.MigrateAsync();
        var users = await db.Users.Include(u => u.Role).ToListAsync();
        Assert.True(users.Single(u => u.Role.Name == "admin").IsEmailVerified);
        Assert.False(users.Single(u => u.Role.Name == "customer").IsEmailVerified);
        Assert.All(users, u => Assert.Equal(0, u.AuthVersion));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Explicit_development_demo_accounts_remain_usable_without_real_email_delivery()
    {
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AuthService>().SeedDevelopmentDataAsync();
        var login = await client.PostAsJsonAsync("/api/users/login", new { email = "admin@foodjs.demo", password = "admin123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.Content.ReadFromJsonAsync<AuthResponse>())!.User.IsEmailVerified);
        Assert.Empty(factory.Emails.Messages);
    }
}
