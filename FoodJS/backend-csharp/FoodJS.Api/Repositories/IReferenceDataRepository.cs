using FoodJS.Api.Models;

namespace FoodJS.Api.Repositories;

// Data needed by assigned services; this does not implement dependency management APIs.
public interface IReferenceDataRepository
{
    Task<User?> FindUserAsync(int id, CancellationToken ct = default);
    Task<bool> UserExistsAsync(int id, CancellationToken ct = default);
    Task<Category?> FindCategoryAsync(int id, CancellationToken ct = default);
    Task<Category?> FindCategoryByNameAsync(string name, CancellationToken ct = default);
    Task<Coupon?> FindCouponAsync(string code, CancellationToken ct = default);
    Task<Address?> FindAddressAsync(int userId, string normalizedText, CancellationToken ct = default);
    Task<OrderStatus?> FindStatusAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListStatusNamesAsync(CancellationToken ct = default);
    void AddCategory(Category category);
    void AddAddress(Address address);
    void AddStatus(OrderStatus status);
}
