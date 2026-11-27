using System.Net;
using System.Net.Http.Json;
using FoodJS.Api.Contracts;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class GoogleEmailVerificationTests : IAsyncLifetime
{
    private readonly AssignedApiFactory factory = new(useTestAuthentication: false);
    private readonly GoogleHttpFactory google = new();
    private HttpClient client = null!;

    public Task InitializeAsync()
    {
        factory.Settings["Google:ClientSecret"] = "test-only-not-a-real-secret";
        factory.OverrideServices = services => services.AddSingleton<IHttpClientFactory>(google);
        client = factory.CreateClient();
        return Task.CompletedTask;
    }
    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); }
    private Task<HttpResponseMessage> GoogleLogin() => client.PostAsJsonAsync("/api/auth/google/callback",
        new { code = "fake-test-code", redirectUri = "http://localhost:5173/auth/callback" });

    [Fact]
    public async Task Google_verified_new_account_is_verified_without_sending_an_extra_email()
    {
        var response = await GoogleLogin();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<AuthResponse>())!.User.IsEmailVerified);
        Assert.Empty(factory.Emails.Messages);
        using var scope = factory.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<FoodJsDbContext>().Users.SingleAsync()).IsEmailVerified);
    }

    [Fact]
    public async Task Google_does_not_activate_an_unverified_password_that_was_registered_for_the_same_email()
    {
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/users/register", new
        { email = google.Email, password = "untrusted-password", firstName = "Pending", lastName = "User" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GoogleLogin()).StatusCode);
        using (var scope = factory.Services.CreateScope())
            Assert.False((await scope.ServiceProvider.GetRequiredService<FoodJsDbContext>().Users.SingleAsync()).IsEmailVerified);
        Assert.Equal(HttpStatusCode.OK, (await factory.ConfirmEmailAsync(client, google.Email)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GoogleLogin()).StatusCode);
    }

    [Fact]
    public async Task Unverified_google_email_is_rejected_without_creating_an_account()
    {
        google.Verified = false;
        Assert.Equal(HttpStatusCode.BadRequest, (await GoogleLogin()).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<FoodJsDbContext>().Users.ToListAsync());
    }

    [Fact]
    public async Task Legacy_google_only_account_can_be_verified_but_deleted_accounts_cannot_return()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            db.Users.Add(new User { Email = google.Email, FirstName = "Google", LastName = "User", PasswordHash = "",
                Role = await db.Roles.SingleAsync(r => r.Name == "customer") });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await GoogleLogin()).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoodJsDbContext>();
            (await db.Users.SingleAsync()).IsDeleted = true;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await GoogleLogin()).StatusCode);
    }

    private sealed class GoogleHttpFactory : HttpMessageHandler, IHttpClientFactory
    {
        public string Email { get; } = "google@example.test";
        public bool Verified { get; set; } = true;
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri!.Host == "oauth2.googleapis.com"
                    ? JsonContent.Create(new { access_token = "test-token" })
                    : JsonContent.Create(new { email = Email, verified_email = Verified, given_name = "Google", family_name = "User" })
            });
    }
}
