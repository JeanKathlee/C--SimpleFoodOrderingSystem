using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;
namespace FoodJS.Api.Repositories;
public sealed class CouponRepository(FoodJsDbContext db) : ICouponRepository
{
    public Task<List<Coupon>> ListAsync(CancellationToken ct) => db.Coupons.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);
    public Task<Coupon?> FindAsync(int id, CancellationToken ct) => db.Coupons.SingleOrDefaultAsync(c => c.Id == id, ct);
    public Task<Coupon?> FindByCodeAsync(string code, CancellationToken ct) => db.Coupons.SingleOrDefaultAsync(c => c.Code == code, ct);
    public Task<bool> CodeExistsAsync(string code, int? exceptId, CancellationToken ct) => db.Coupons.IgnoreQueryFilters().AnyAsync(c => c.Code.ToUpper() == code && (exceptId == null || c.Id != exceptId), ct);
    public void Add(Coupon coupon) => db.Coupons.Add(coupon);
    public Task<bool> AnyIncludingDeletedAsync(CancellationToken ct) => db.Coupons.IgnoreQueryFilters().AnyAsync(ct);
}
