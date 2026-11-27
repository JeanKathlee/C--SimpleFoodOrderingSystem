using FoodJS.Api.Models;

namespace FoodJS.Api.Repositories;

public interface IMenuRepository
{
    Task<IReadOnlyList<MenuItem>> ListAsync(bool includeUnavailable, string? search, string? category, CancellationToken ct = default);
    Task<MenuItem?> FindAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<MenuItem>> FindManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);
    Task<bool> AnyIncludingDeletedAsync(CancellationToken ct = default);
    Task<int> CountAsync(MenuQuery query, CancellationToken ct);
    Task<IReadOnlyList<MenuItem>> PageAsync(MenuQuery query, int limit, int offset, CancellationToken ct);
    void Add(MenuItem item);
}
public sealed record MenuQuery(int? CategoryId, bool? IsAvailable, string? Search);
