using System.Net.Mail;
using FoodJS.Api.Data;
using FoodJS.Api.Features.Auth;
using FoodJS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FoodJS.Api.Features.Users;

// when something is wrong this throws BadHttpRequestException (400), the controller sends the message back
public sealed class UsersService(FoodJsDbContext db, AuthService authService)
{
    public async Task<List<AuthUserResponse>> GetAllAsync()
    {
        var users = await db.Users.Include(u => u.Role).OrderBy(u => u.Id).ToListAsync();

        // ponytail: one address query per user, fine for a class project, load addresses in one query if the list gets big
        var result = new List<AuthUserResponse>();
        foreach (var user in users)
        {
            result.Add(await authService.ToResponseAsync(user));
        }
        return result;
    }

    public async Task<AuthUserResponse?> GetAsync(int id)
    {
        var user = await FindAsync(id);
        return user is null ? null : await authService.ToResponseAsync(user);
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var email = request.Email?.Trim() ?? "";
        var firstName = request.FirstName?.Trim() ?? "";
        var lastName = request.LastName?.Trim() ?? "";
        var password = request.Password ?? "";

        if (email == "" || password == "" || firstName == "" || lastName == "")
        {
            throw new BadHttpRequestException("Email, password, first name, and last name are required.");
        }
        if (!MailAddress.TryCreate(email, out _))
        {
            throw new BadHttpRequestException("Please enter a valid email.");
        }
        if (password.Length < 6)
        {
            throw new BadHttpRequestException("Password must be at least 6 characters.");
        }
        if (await authService.FindByEmailAsync(email) is not null)
        {
            throw new BadHttpRequestException("Email already in use.");
        }

        // new accounts are always customers, admins can't be made from the register page
        var user = new User
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            PasswordHash = "",
            MobileNumber = request.MobileNumber?.Trim(),
            Role = await db.Roles.SingleAsync(r => r.Name == AuthService.CustomerRole),
        };
        user.PasswordHash = AuthService.Hasher.HashPassword(user, password);
        db.Users.Add(user);
        if (request.Address is not null)
        {
            await SetMainAddressAsync(user, request.Address);
        }
        await db.SaveChangesAsync();

        // log them in right away, same as the old node api
        return await authService.CreateSessionAsync(user, "Account created successfully.");
    }

    // returns null if the user doesn't exist
    public async Task<AuthUserResponse?> UpdateAsync(int id, UpdateProfileRequest request)
    {
        var user = await FindAsync(id);
        if (user is null)
        {
            return null;
        }

        if (request.FirstName is not null)
        {
            user.FirstName = request.FirstName.Trim();
            if (user.FirstName == "")
            {
                throw new BadHttpRequestException("First name cannot be empty.");
            }
        }
        if (request.LastName is not null)
        {
            user.LastName = request.LastName.Trim();
            if (user.LastName == "")
            {
                throw new BadHttpRequestException("Last name cannot be empty.");
            }
        }
        if (request.MobileNumber is not null)
        {
            user.MobileNumber = request.MobileNumber.Trim();
        }
        if (request.ProfileImage is not null)
        {
            user.ProfileImage = request.ProfileImage.Trim();
        }
        if (request.Address is not null)
        {
            await SetMainAddressAsync(user, request.Address);
        }

        await db.SaveChangesAsync();
        return await authService.ToResponseAsync(user);
    }

    // returns false if the user doesn't exist
    public async Task<bool> DeleteAsync(int id, int currentUserId)
    {
        if (id == currentUserId)
        {
            throw new BadHttpRequestException("You cannot delete your own account.");
        }

        var user = await FindAsync(id);
        if (user is null)
        {
            return false;
        }

        // deleting a user also deletes everything linked to them (orders, cart, addresses).
        // admins also own the menu items they made, so admins are never deleted here
        if (user.Role.Name == AuthService.AdminRole)
        {
            throw new BadHttpRequestException("Admin accounts cannot be deleted.");
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return true;
    }

    private Task<User?> FindAsync(int id) =>
        db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id);

    // the app only has one address text box, so we keep it as one line and leave city empty.
    // an empty text removes the address
    private async Task SetMainAddressAsync(User user, string text)
    {
        text = text.Trim();
        var main = await db.Addresses.FirstOrDefaultAsync(a => a.UserId == user.Id && a.IsDefault);

        if (text == "")
        {
            if (main is not null)
            {
                db.Addresses.Remove(main);
            }
        }
        else if (main is null)
        {
            db.Addresses.Add(new Address { User = user, AddressLine1 = text, City = "", IsDefault = true });
        }
        else
        {
            main.AddressLine1 = text;
        }
    }
}
