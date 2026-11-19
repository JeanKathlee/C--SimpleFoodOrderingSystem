namespace FoodJS.Api.Features.Menu;

public interface IMenuService
{
    Task<IReadOnlyList<MenuItemResponse>> GetAllAsync(CancellationToken ct = default);

    Task<MenuItemResponse> CreateAsync(SaveMenuItemRequest request, int? adminId, CancellationToken ct = default);

    /// <summary>Returns null when the item does not exist.</summary>
    Task<MenuItemResponse?> UpdateAsync(int id, SaveMenuItemRequest request, CancellationToken ct = default);

    /// <summary>Returns null when the item does not exist.</summary>
    Task<MenuItemResponse?> SetAvailabilityAsync(int id, bool isAvailable, CancellationToken ct = default);

    /// <summary>Returns false when the item does not exist.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
