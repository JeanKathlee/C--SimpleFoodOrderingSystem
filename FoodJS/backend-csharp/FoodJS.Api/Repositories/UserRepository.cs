using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;
namespace FoodJS.Api.Repositories;

public sealed class UserRepository(FoodJsDbContext db) : IUserRepository
{
    public Task<List<User>> ListAsync() => db.Users.Include(u => u.Role).OrderBy(u => u.Id).ToListAsync();
    public Task<User?> FindAsync(int id) => db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id);
    public Task<User?> FindByEmailAsync(string email) => db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
    public Task<bool> EmailExistsAsync(string email, int exceptId) => db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id != exceptId && u.Email.ToLower() == email.ToLower());
    public Task<Role> GetRoleAsync(string name) => db.Roles.SingleAsync(r => r.Name == name);
    public Task<List<string>> RoleNamesAsync() => db.Roles.IgnoreQueryFilters().Select(r => r.Name).ToListAsync();
    public Task<string[]> AddressLinesAsync(int userId) => db.Addresses.Where(a => a.UserId == userId).OrderByDescending(a => a.IsDefault).Select(a => a.AddressLine1).ToArrayAsync();
    public Task<Address?> DefaultAddressAsync(int userId) => db.Addresses.FirstOrDefaultAsync(a => a.UserId == userId && a.IsDefault);
    public Task<List<PasswordResetToken>> UnusedResetTokensAsync(int userId) => db.PasswordResetTokens.Where(t => t.UserId == userId && t.UsedAt == null).ToListAsync();
    public Task<PasswordResetToken?> FindResetTokenAsync(int userId, string hash) => db.PasswordResetTokens.FirstOrDefaultAsync(t => t.UserId == userId && t.TokenHash == hash && t.UsedAt == null);
    public void Add(User user) => db.Users.Add(user);
    public void AddRole(Role role) => db.Roles.Add(role);
    public void AddAddress(Address address) => db.Addresses.Add(address);
    public void AddResetToken(PasswordResetToken token) => db.PasswordResetTokens.Add(token);
}
