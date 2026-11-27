namespace FoodJS.Api.Models;

public sealed class EmailVerificationToken : ISoftDeletable
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public required string Email { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public bool IsDeleted { get; set; }
}
