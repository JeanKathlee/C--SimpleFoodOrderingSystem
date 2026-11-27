using FoodJS.Api.Models;
namespace FoodJS.Api.Repositories;
public interface ICouponRepository
{
    Task<List<Coupon>> ListAsync(CancellationToken ct);
    Task<Coupon?> FindAsync(int id, CancellationToken ct);
    Task<Coupon?> FindByCodeAsync(string code, CancellationToken ct);
    Task<bool> CodeExistsAsync(string code, int? exceptId, CancellationToken ct);
    void Add(Coupon coupon);
    Task<bool> AnyIncludingDeletedAsync(CancellationToken ct);
}
