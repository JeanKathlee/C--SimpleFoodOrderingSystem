namespace FoodJS.Api.Models;

public class Address
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public required string AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public required string City { get; set; }

    public string? Province { get; set; }

    public string? PostalCode { get; set; }

    public string Country { get; set; } = "Philippines";

    public bool IsDefault { get; set; }
}