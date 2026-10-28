namespace FoodJS.Api.Models;

public class User
{
    public int Id { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    public string? MobileNumber { get; set; }

    public string? ProfileImage { get; set; }

    public int RoleId { get; set; }

    public Role Role { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}