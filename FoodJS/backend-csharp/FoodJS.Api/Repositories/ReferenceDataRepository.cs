using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Repositories;

public sealed class ReferenceDataRepository(FoodJsDbContext db) : IReferenceDataRepository
{
    public Task<User?> FindUserAsync(int id, CancellationToken ct = default) => db.Users.FirstOrDefaultAsync(user => user.Id == id, ct);
    public Task<bool> UserExistsAsync(int id, CancellationToken ct = default) => db.Users.AnyAsync(user => user.Id == id, ct);
    public Task<Category?> FindCategoryAsync(int id, CancellationToken ct = default) => db.Categories.FirstOrDefaultAsync(category => category.Id == id, ct);
    public Task<Category?> FindCategoryByNameAsync(string name, CancellationToken ct = default) => db.Categories.FirstOrDefaultAsync(category => category.Name == name, ct);
    public Task<Coupon?> FindCouponAsync(string code, CancellationToken ct = default) => db.Coupons.FirstOrDefaultAsync(coupon => coupon.Code == code, ct);
    public Task<Address?> FindAddressAsync(int userId, string normalizedText, CancellationToken ct = default) =>
        db.Addresses.FirstOrDefaultAsync(address => address.UserId == userId && address.AddressLine1.ToLower() == normalizedText, ct);
    public Task<OrderStatus?> FindStatusAsync(string name, CancellationToken ct = default) => db.OrderStatuses.FirstOrDefaultAsync(status => status.Name == name, ct);
    public async Task<IReadOnlyList<string>> ListStatusNamesAsync(CancellationToken ct = default) => await db.OrderStatuses.Select(status => status.Name).ToListAsync(ct);
    public void AddCategory(Category category) => db.Categories.Add(category);
    public void AddAddress(Address address) => db.Addresses.Add(address);
    public void AddStatus(OrderStatus status) => db.OrderStatuses.Add(status);
}
