using FoodJS.Api.Models;
namespace FoodJS.Api.Repositories;

public interface IUserRepository
{
    Task<List<User>> ListAsync();
    Task<User?> FindAsync(int id);
    Task<User?> FindByEmailAsync(string email);
    Task<bool> EmailExistsAsync(string email, int exceptId);
    Task<Role> GetRoleAsync(string name);
    Task<List<string>> RoleNamesAsync();
    Task<string[]> AddressLinesAsync(int userId);
    Task<Address?> DefaultAddressAsync(int userId);
    Task<List<PasswordResetToken>> UnusedResetTokensAsync(int userId);
    Task<PasswordResetToken?> LatestResetTokenAsync(int userId);
    Task<PasswordResetToken?> FindResetTokenAsync(int userId, string hash);
    Task<List<EmailVerificationToken>> UnusedVerificationTokensAsync(int userId);
    Task<EmailVerificationToken?> LatestVerificationTokenAsync(int userId);
    Task<EmailVerificationToken?> FindVerificationTokenAsync(string hash);
    void Add(User user);
    void AddRole(Role role);
    void AddAddress(Address address);
    void AddResetToken(PasswordResetToken token);
    void AddVerificationToken(EmailVerificationToken token);
}
