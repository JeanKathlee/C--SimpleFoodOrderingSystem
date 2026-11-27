using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodJS.Api.Contracts;
using FoodJS.Api.Data;
using FoodJS.Api.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class PasswordRecoveryTests : IAsyncLifetime
{
    private const string Email = "recovery@example.test";
    private readonly AssignedApiFactory factory = new(useTestAuthentication: false);
    private HttpClient client = null!;
    public Task InitializeAsync() { client = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); }
    private Task<HttpResponseMessage> Request(string email = Email) => client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
    private Task<HttpResponseMessage> Reset(string token, string email = Email, string password = "replacement-password") =>
        client.PostAsJsonAsync("/api/auth/reset-password", new { email, token, newPassword = password });
    private string Token() => QueryHelpers.ParseQuery(factory.Emails.PasswordResets.Last().Link.Fragment.TrimStart('#'))["token"].ToString();
    private async Task Register(bool verify = true)
    {
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/users/register", new
        { firstName = "Recovery", lastName = "Test", email = Email, password = "original-password" })).StatusCode);
        if (verify) Assert.Equal(HttpStatusCode.OK, (await factory.ConfirmEmailAsync(client, Email)).StatusCode);
    }

    [Fact]
    public async Task Reset_is_emailed_not_returned_and_only_a_hash_is_stored()
    {
        await Register();
        var response = await Request("  RECOVERY@example.test  ");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(payload.TryGetProperty("resetToken", out _));
        var link = Assert.Single(factory.Emails.PasswordResets).Link;
        Assert.Equal("/reset-password", link.AbsolutePath); Assert.Equal("", link.Query);
        Assert.Equal(64, Token().Length);
        using var scope = factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<FoodJsDbContext>().PasswordResetTokens.SingleAsync();
        Assert.NotEqual(Token(), row.TokenHash); Assert.Equal(64, row.TokenHash.Length);
        Assert.Equal(TimeSpan.FromMinutes(30), row.ExpiresAt - row.CreatedAt); Assert.False(row.IsDeleted);
    }

    [Fact]
    public async Task Used_links_cannot_replay_old_sessions_are_revoked_and_notification_is_sent()
    {
        await Register();
        var session = (await (await client.PostAsJsonAsync("/api/users/login", new { email = Email, password = "original-password" }))
            .Content.ReadFromJsonAsync<AuthResponse>())!;
        await Request(); var token = Token();
        Assert.Equal(HttpStatusCode.OK, (await Reset(token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(token)).StatusCode);
        Assert.Equal(Email, Assert.Single(factory.Emails.PasswordChanges));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/users/login", new { email = Email, password = "original-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/users/login", new { email = Email, password = "replacement-password" })).StatusCode);
    }

    [Fact]
    public async Task Resends_are_cooled_down_and_rotate_previous_links()
    {
        await Register(); await Request(); var first = Token();
        var pending = await (await Request()).Content.ReadAsStringAsync();
        var missing = await (await Request("missing@example.test")).Content.ReadAsStringAsync();
        Assert.Equal(pending, missing); Assert.Single(factory.Emails.PasswordResets);
        factory.Clock.Advance(TimeSpan.FromSeconds(61)); await Request();
        var replacement = Token(); Assert.NotEqual(first, replacement);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Reset(replacement)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<FoodJsDbContext>().PasswordResetTokens.ToListAsync();
        Assert.Equal(2, rows.Count); Assert.All(rows, row => { Assert.NotNull(row.UsedAt); Assert.False(row.IsDeleted); });
    }

    [Fact]
    public async Task Links_expire_at_thirty_minutes_and_do_not_change_passwords()
    {
        await Register(); await Request(); var token = Token();
        factory.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(token)).StatusCode);
        Assert.Empty(factory.Emails.PasswordChanges);
    }

    [Fact]
    public async Task Pending_missing_and_google_only_accounts_do_not_receive_reset_links()
    {
        await Register(false);
        var pending = await (await Request()).Content.ReadAsStringAsync();
        Assert.Equal(pending, await (await Request("missing@example.test")).Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            var user = await db.Users.SingleAsync(); user.IsEmailVerified = true; user.PasswordHash = "";
            await db.SaveChangesAsync();
        }
        Assert.Equal(pending, await (await Request()).Content.ReadAsStringAsync());
        Assert.Empty(factory.Emails.PasswordResets);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public async Task Malformed_tokens_are_rejected(string token) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(token)).StatusCode);

    [Fact]
    public async Task Wrong_email_and_invalid_password_cannot_consume_a_valid_link()
    {
        await Register(); await Request(); var token = Token();
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(token, "someone@example.test")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(token, password: "123")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(token, password: new string('a', 129))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Reset(token)).StatusCode);
    }

    [Fact]
    public async Task Email_changes_and_soft_deleted_tokens_invalidate_old_links()
    {
        await Register(); await Request(); var token = Token();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            (await db.Users.SingleAsync()).Email = "changed@example.test";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(token, "changed@example.test")).StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(61)); await Request("changed@example.test"); token = Token();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            (await db.PasswordResetTokens.OrderByDescending(t => t.Id).FirstAsync()).IsDeleted = true;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(token, "changed@example.test")).StatusCode);
    }

    [Fact]
    public async Task Missing_configuration_is_explicit_for_every_email_and_delivery_failure_is_generic()
    {
        await Register(); factory.Emails.Configured = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Request()).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Request("missing@example.test")).StatusCode);
        factory.Emails.Configured = true; factory.Emails.FailDelivery = true;
        var failed = await (await Request()).Content.ReadAsStringAsync();
        Assert.Equal(failed, await (await Request("missing@example.test")).Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<FoodJsDbContext>().PasswordResetTokens.SingleAsync();
        Assert.NotNull(row.UsedAt); Assert.Empty(factory.Emails.PasswordResets);
    }

    [Fact]
    public async Task Notification_failure_does_not_undo_a_completed_password_reset()
    {
        await Register(); await Request(); var token = Token(); factory.Emails.FailDelivery = true;
        Assert.Equal(HttpStatusCode.OK, (await Reset(token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(token)).StatusCode);
    }

    [Theory]
    [InlineData("http://evil.example.test")]
    [InlineData("https://foodjs.example.test/redirect?url=evil")]
    public async Task Untrusted_frontend_urls_are_rejected(string url)
    {
        factory.Services.GetRequiredService<IConfiguration>()["Email:FrontendUrl"] = url;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Request()).StatusCode);
        Assert.Empty(factory.Emails.PasswordResets);
    }

    [Theory]
    [InlineData("forgot-password")]
    [InlineData("reset-password")]
    public async Task Both_recovery_endpoints_rate_limit_abuse(string endpoint)
    {
        HttpResponseMessage? response = null;
        for (var i = 0; i < 11; i++) response = await client.PostAsJsonAsync($"/api/auth/{endpoint}", new { email = "missing@example.test" });
        Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
    }
}
