namespace FoodJS.Api.Models;

/// <summary>Persist a tombstone instead of physically deleting an application record.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}
