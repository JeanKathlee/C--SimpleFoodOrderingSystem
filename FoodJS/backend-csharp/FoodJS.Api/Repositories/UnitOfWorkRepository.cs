using FoodJS.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FoodJS.Api.Repositories;

public sealed class UnitOfWorkRepository(FoodJsDbContext db) : IUnitOfWorkRepository
{
    public async Task SaveAsync(CancellationToken ct = default)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 } sqlite
            && sqlite.Message.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase))
        {
            throw new FoodJS.Api.Common.ApiException(409, "This record is referenced by retained data and cannot be permanently deleted.");
        }
    }
    public async Task<IRepositoryTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
        new RepositoryTransaction(await db.Database.BeginTransactionAsync(ct));
    public Task MigrateAsync(CancellationToken ct = default) => db.Database.MigrateAsync(ct);

    private sealed class RepositoryTransaction(IDbContextTransaction transaction) : IRepositoryTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => transaction.CommitAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
