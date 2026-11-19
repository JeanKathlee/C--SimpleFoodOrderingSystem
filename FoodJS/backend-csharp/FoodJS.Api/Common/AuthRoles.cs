using System.Security.Claims;

namespace FoodJS.Api.Common;

public static class AuthRoles
{
    // Must match the "role" claim value the JWT carries (the Node API used lower case).
    public const string Admin = "admin";
    public const string Customer = "customer";
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>The JWT "sub" claim (or NameIdentifier when claims are mapped).</summary>
    public static int? GetUserId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out var id) ? id : null;
    }

    public static int RequireUserId(this ClaimsPrincipal user) =>
        user.GetUserId() ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Unauthorized. Token required.");

    public static string GetRole(this ClaimsPrincipal user) =>
        (user.FindFirst("role")?.Value ?? user.FindFirst(ClaimTypes.Role)?.Value ?? AuthRoles.Customer)
        .ToLowerInvariant();
}
