using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Models;

// Kept even if an order is deleted: retrying an old submission must not create it again.
[PrimaryKey(nameof(UserId), nameof(Key))]
public sealed class CheckoutSubmission : ISoftDeletable
{
    public int UserId { get; set; }
    public required string Key { get; set; }
    public required string RequestHash { get; set; }
    public int OrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsDeleted { get; set; }
}
