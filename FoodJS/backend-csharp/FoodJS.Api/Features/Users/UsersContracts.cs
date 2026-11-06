namespace FoodJS.Api.Features.Users;

// same fields the register page sends
public sealed record RegisterRequest(
    string? Email,
    string? Password,
    string? FirstName,
    string? LastName,
    string? MobileNumber,
    string? Address);

// every field is optional, only the ones sent get changed
public sealed record UpdateProfileRequest(
    string? FirstName,
    string? LastName,
    string? MobileNumber,
    string? Address,
    string? ProfileImage);
