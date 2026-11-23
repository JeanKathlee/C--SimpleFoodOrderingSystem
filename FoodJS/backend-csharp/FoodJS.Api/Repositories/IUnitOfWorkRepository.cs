namespace FoodJS.Api.Repositories;

public interface IRepositoryTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}

// Repositories share one scoped context. Services choose the transaction boundary
// without accessing EF, the database connection, or SaveChanges directly.
public interface IUnitOfWorkRepository
{
    Task SaveAsync(CancellationToken ct = default);
    Task<IRepositoryTransaction> BeginTransactionAsync(CancellationToken ct = default);
    Task MigrateAsync(CancellationToken ct = default);
}
