using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using FoodJS.Api.Common;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Development;

// Local migration adapter, not a replacement for the team's dependency APIs.
public sealed class MigrationDependencies(HttpClient reference, FoodJsDbContext db)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static int CategoryKey(string id)
    {
        if (PublicId.TryParseCategory(id, out var numeric)) return numeric;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return 100_000_000 + (int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash) % 1_000_000_000);
    }

    public async Task<IReadOnlyList<object>> CategoriesAsync(CancellationToken ct)
    {
        var source = await ReadAsync<ReferenceCategory>("api/categories", ct);
        await Gate.WaitAsync(ct);
        try
        {
            await SyncCategoriesAsync(source, ct);
            await db.SaveChangesAsync(ct);
            return source.Select(item => (object)new { id = PublicId.Category(CategoryKey(item.Id)), item.Name, item.Description }).ToList();
        }
        finally { Gate.Release(); }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var categories = await ReadAsync<ReferenceCategory>("api/categories", ct);
        var menu = await ReadAsync<ReferenceMenu>("api/menu", ct);
        var coupons = await ReadAsync<ReferenceCoupon>("api/coupons", ct);
        await Gate.WaitAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS __LocalMigrationState (Name TEXT PRIMARY KEY)", ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await SyncCategoriesAsync(categories, ct);
            var initialized = await db.Database.SqlQueryRaw<string>("SELECT Name AS Value FROM __LocalMigrationState WHERE Name = 'initial-menu'").AnyAsync(ct);
            if (!initialized)
            {
                if (!await db.MenuItems.AnyAsync(ct))
                {
                    foreach (var item in menu)
                    {
                        if (!PublicId.TryParseMenu(item.Id, out var id)) throw new InvalidOperationException($"Cannot map reference menu ID {item.Id}.");
                        db.MenuItems.Add(new MenuItem { Id = id, Name = item.Name, Description = item.Description,
                            CategoryId = CategoryKey(item.CategoryId), Price = item.Price, PrepTime = item.PrepTime,
                            IsAvailable = item.IsAvailable, SoldCount = item.SoldCount, Image = item.Image });
                    }
                }
                await db.Database.ExecuteSqlRawAsync("INSERT INTO __LocalMigrationState (Name) VALUES ('initial-menu')", ct);
            }
            await SyncCouponsAsync(coupons, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        finally { Gate.Release(); }
    }

    public async Task RefreshCouponsAsync(CancellationToken ct)
    {
        var coupons = await ReadAsync<ReferenceCoupon>("api/coupons", ct);
        await Gate.WaitAsync(ct);
        try { await SyncCouponsAsync(coupons, ct); await db.SaveChangesAsync(ct); }
        finally { Gate.Release(); }
    }

    public async Task<ReferenceProfile?> AuthenticateAsync(string bearer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/users/me");
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(bearer);
        using var response = await reference.SendAsync(request, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) return null;
        response.EnsureSuccessStatusCode();
        var profile = await response.Content.ReadFromJsonAsync<ReferenceProfile>(ct);
        if (profile is null || profile.Id <= 0 || string.IsNullOrWhiteSpace(profile.Email)
            || profile.Role is not (AuthRoles.Admin or AuthRoles.Customer)) return null;
        await Gate.WaitAsync(ct);
        try
        {
            var role = await db.Roles.SingleOrDefaultAsync(item => item.Name == profile.Role, ct);
            if (role is null) { role = new Role { Name = profile.Role }; db.Roles.Add(role); }
            var user = await db.Users.FindAsync([profile.Id], ct);
            if (user is null)
            {
                user = new User { Id = profile.Id, FirstName = "", LastName = "", Email = profile.Email,
                    PasswordHash = "LOCAL-BRIDGE-NO-PASSWORD-LOGIN" };
                db.Users.Add(user);
            }
            user.FirstName = profile.FirstName; user.LastName = profile.LastName; user.Email = profile.Email;
            user.MobileNumber = profile.MobileNumber; user.Role = role;
            await db.SaveChangesAsync(ct);
            return profile;
        }
        finally { Gate.Release(); }
    }

    private async Task<List<T>> ReadAsync<T>(string path, CancellationToken ct) =>
        await reference.GetFromJsonAsync<List<T>>(path, ct) ?? throw new InvalidOperationException($"Reference API returned no data for {path}.");

    private async Task SyncCategoriesAsync(List<ReferenceCategory> source, CancellationToken ct)
    {
        if (source.GroupBy(item => CategoryKey(item.Id)).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Reference category IDs collide; migration bridge cannot map them safely.");
        foreach (var item in source)
        {
            var id = CategoryKey(item.Id);
            var category = await db.Categories.FindAsync([id], ct);
            if (category is null) { category = new Category { Id = id, Name = item.Name }; db.Categories.Add(category); }
            category.Name = item.Name; category.Description = item.Description;
        }
    }

    private async Task SyncCouponsAsync(List<ReferenceCoupon> source, CancellationToken ct)
    {
        var codes = source.Select(item => item.Code).ToList();
        foreach (var removed in await db.Coupons.Where(item => !codes.Contains(item.Code)).ToListAsync(ct)) removed.IsActive = false;
        foreach (var item in source)
        {
            var coupon = await db.Coupons.SingleOrDefaultAsync(entry => entry.Code == item.Code, ct);
            if (coupon is null) { coupon = new Coupon { Code = item.Code, DiscountType = item.DiscountType }; db.Coupons.Add(coupon); }
            coupon.Discount = item.Discount; coupon.DiscountType = item.DiscountType;
            coupon.MinimumOrderAmount = item.MinOrderAmount;
            coupon.ExpiryDate = DateOnly.TryParse(item.ExpiryDate, out var expiry) ? expiry : DateOnly.MinValue;
            coupon.MaximumUses = item.MaxUses < 0 ? null : item.MaxUses;
            coupon.UsedCount = Math.Max(coupon.UsedCount, item.UsedCount);
            coupon.Description = item.Description; coupon.IsActive = item.IsActive;
        }
    }
}

public sealed record ReferenceCategory(string Id, string Name, string Description = "");
public sealed record ReferenceMenu(string Id, string Name, string CategoryId, decimal Price, int PrepTime,
    string Description = "", bool IsAvailable = true, int SoldCount = 0, string? Image = null);
public sealed record ReferenceCoupon(string Code, decimal Discount, string DiscountType, string ExpiryDate,
    decimal MinOrderAmount = 0, int MaxUses = -1, int UsedCount = 0, string Description = "", bool IsActive = true);
public sealed record ReferenceProfile(int Id, string Email, string Role, string FirstName = "", string LastName = "", string? MobileNumber = null);
