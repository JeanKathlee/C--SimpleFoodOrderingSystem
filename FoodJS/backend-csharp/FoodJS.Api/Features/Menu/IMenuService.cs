namespace FoodJS.Api.Features.Menu;

public interface IMenuService
{
    // showHidden includes unavailable items for admins, never soft-deleted records.
    Task<IReadOnlyList<MenuItemResponse>> GetAllAsync(bool showHidden, string? search = null, string? category = null, CancellationToken ct = default);

    Task<MenuItemResponse?> GetAsync(int id, bool showHidden, CancellationToken ct = default);

    Task SeedDevelopmentDataAsync(string contentRoot, CancellationToken ct = default);

    Task<MenuItemResponse> CreateAsync(SaveMenuItemRequest request, int? adminId, CancellationToken ct = default);

    /// <summary>Returns null when the item does not exist or was soft-deleted.</summary>
    Task<MenuItemResponse?> UpdateAsync(int id, SaveMenuItemRequest request, CancellationToken ct = default);

    /// <summary>Returns null when the item does not exist or was soft-deleted.</summary>
    Task<MenuItemResponse?> SetAvailabilityAsync(int id, bool? isAvailable, CancellationToken ct = default);

    /// <summary>Soft-deletes the item. Returns false when missing or already deleted.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
