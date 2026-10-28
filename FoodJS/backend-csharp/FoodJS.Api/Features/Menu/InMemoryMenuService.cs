using System.Collections.Concurrent;

namespace FoodJS.Api.Features.Menu;

public sealed class InMemoryMenuService : IMenuService
{
    private readonly ConcurrentDictionary<string, MenuItemResponse> _items = new();
    private int _nextId = 1000;

    public IReadOnlyCollection<MenuItemResponse> GetAll() =>
        _items.Values.OrderBy(item => item.Name).ToArray();

    public MenuItemResponse Create(SaveMenuItemRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var id = $"M-{Interlocked.Increment(ref _nextId)}";
        var item = new MenuItemResponse(
            id,
            request.Name.Trim(),
            request.Description.Trim(),
            request.CategoryId,
            request.CategoryId, // Replace with the category name from the database.
            request.Price,
            request.PrepTime,
            request.IsAvailable,
            0,
            request.Image,
            now,
            now);

        _items[id] = item;
        return item;
    }

    public MenuItemResponse? Update(string id, SaveMenuItemRequest request)
    {
        if (!_items.TryGetValue(id, out var existing))
        {
            return null;
        }

        var updated = existing with
        {
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            CategoryId = request.CategoryId,
            Category = request.CategoryId,
            Price = request.Price,
            PrepTime = request.PrepTime,
            IsAvailable = request.IsAvailable,
            Image = request.Image,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _items[id] = updated;
        return updated;
    }

    public bool Delete(string id) => _items.TryRemove(id, out _);
}
