namespace FoodJS.Api.Contracts;
public sealed record AdminInsightsResponse(int TotalOrders, decimal TotalRevenue, int TotalCustomers, int TodayOrders, DateTimeOffset LastUpdated);
