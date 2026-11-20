using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FoodJS.Api.Development;

public sealed class MigrationAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    MigrationDependencies dependencies)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "LocalMigrationBridge";
    private bool unavailable;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var bearer = Request.Headers.Authorization.ToString();
        if (!bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(bearer[7..]))
            return AuthenticateResult.NoResult();
        try
        {
            // Identity/role come from the existing profile API, never from client claims.
            var profile = await dependencies.AuthenticateAsync(bearer, Context.RequestAborted);
            if (profile is null) return AuthenticateResult.Fail("Reference API rejected the session.");
            var identity = new ClaimsIdentity([
                new Claim("sub", profile.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim("role", profile.Role), new Claim(ClaimTypes.Role, profile.Role)
            ], SchemeName);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
        {
            unavailable = true;
            return AuthenticateResult.Fail("Local reference API is unavailable.");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = unavailable ? 503 : 401;
        return Response.WriteAsJsonAsync(new { message = unavailable
            ? "The local migration bridge cannot reach Express on port 3001. Start npm run dev:backend."
            : "Your session is required. Please log in again." });
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(new {
            message = "Administrator access is required. Sign out and sign in again using your admin account."
        });
    }
}
