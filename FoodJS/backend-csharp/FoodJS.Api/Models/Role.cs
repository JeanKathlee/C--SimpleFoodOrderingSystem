namespace FoodJS.Api.Models;

public class Role : ISoftDeletable
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public bool IsDeleted { get; set; }
}
