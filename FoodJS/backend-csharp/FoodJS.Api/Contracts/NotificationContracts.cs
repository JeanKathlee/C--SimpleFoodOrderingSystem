namespace FoodJS.Api.Contracts;
public sealed record NotificationResponse(int Id, int UserId, string? OrderId, string Title, string Message, string Status, string Channel, bool Read, DateTimeOffset CreatedAt);
