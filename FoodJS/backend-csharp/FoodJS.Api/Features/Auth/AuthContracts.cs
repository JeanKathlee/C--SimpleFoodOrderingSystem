using System.Text.Json.Serialization;

namespace FoodJS.Api.Features.Auth;

public sealed record LoginRequest(string? Email, string? Password);

public sealed record GoogleCallbackRequest(string? Code, string? RedirectUri);

// same fields the old node api sent back, so the react app still works
public sealed record AuthUserResponse(
    int Id,
    string FirstName,
    string LastName,
    string Name,
    string Email,
    string Role,
    string MobileNumber,
    string Address,
    string[] AddressBook,
    string ProfileImage,
    string LastOrderAddress,
    string LastOrderMobileNumber,
    DateTimeOffset CreatedAt);

public sealed record AuthResponse(string Message, string Token, AuthUserResponse User);

internal sealed record GoogleUserInfo(
    string? Email,
    [property: JsonPropertyName("verified_email")] bool VerifiedEmail,
    [property: JsonPropertyName("given_name")] string? GivenName,
    [property: JsonPropertyName("family_name")] string? FamilyName);
