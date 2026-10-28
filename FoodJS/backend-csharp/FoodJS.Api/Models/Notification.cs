namespace FoodJS.Api.Models;

public class Notification
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public int? OrderId { get; set; }

    public Order? Order { get; set; }

    public required string Title { get; set; }

    public required string Message { get; set; }

    public string Type { get; set; } = "Info";

    public bool IsRead { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}