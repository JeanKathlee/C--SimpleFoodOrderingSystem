using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class AuthFlowTests : IAsyncLifetime
{
    private readonly AssignedApiFactory factory = new();
    private HttpClient client = null!;

    public Task InitializeAsync()
    {
        client = factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task Register_login_and_one_time_password_reset_work_end_to_end()
    {
        const string email = "auth-flow@example.com";
        var registered = await client.PostAsJsonAsync("/api/users/register", new
        {
            firstName = "Auth",
            lastName = "Flow",
            email,
            password = "initial-password"
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Assert.False((await Json(registered)).TryGetProperty("token", out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await Login(email, "initial-password")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ConfirmEmailAsync(client, email)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Login(email, "initial-password")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(email, "wrong-password")).StatusCode);

        var forgot = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        Assert.False((await Json(forgot)).TryGetProperty("resetToken", out _));
        var resetToken = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
            factory.Emails.PasswordResets.Last().Link.Fragment.TrimStart('#'))["token"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(resetToken));

        var resetBody = new { email, token = resetToken, newPassword = "new-password" };
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/reset-password", resetBody)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/reset-password", resetBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(email, "initial-password")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(email, "new-password")).StatusCode);
    }

    [Fact]
    public async Task Registration_rejects_an_invalid_email()
    {
        var response = await client.PostAsJsonAsync("/api/users/register", new
        {
            firstName = "Bad",
            lastName = "Email",
            email = "not-an-email",
            password = "password"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<HttpResponseMessage> Login(string email, string password) =>
        client.PostAsJsonAsync("/api/users/login", new { email, password });

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>());
}
