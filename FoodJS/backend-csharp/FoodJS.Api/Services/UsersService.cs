using FoodJS.Api.Contracts;
using System.Net.Mail;
using FoodJS.Api.Common;
using FoodJS.Api.Repositories;
using FoodJS.Api.Models;

namespace FoodJS.Api.Services;

// when something is wrong this throws BadHttpRequestException (400), the controller sends the message back
public sealed class UsersService(IUserRepository repository, IUnitOfWorkRepository unitOfWork, AuthService authService)
{
    public async Task<List<AuthUserResponse>> GetAllAsync()
    {
        var users = await repository.ListAsync();

        // Build response DTOs through the auth service; address reads stay in the repository.
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
        if (await repository.EmailExistsAsync(email, 0))
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
            Role = await repository.GetRoleAsync(AuthService.CustomerRole),
        };
        user.PasswordHash = AuthService.Hasher.HashPassword(user, password);
        repository.Add(user);
        if (request.Address is not null)
        {
            await SetMainAddressAsync(user, request.Address);
        }
        await unitOfWork.SaveAsync();

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
        if (request.Email is not null)
        {
            var email = request.Email.Trim();
            if (!MailAddress.TryCreate(email, out _))
            {
                throw new BadHttpRequestException("Please enter a valid email.");
            }

            var duplicate = await repository.EmailExistsAsync(email, id);
            if (duplicate)
            {
                throw new BadHttpRequestException("Email already in use.");
            }

            user.Email = email;
        }
        if (request.MobileNumber is not null)
        {
            user.MobileNumber = request.MobileNumber.Trim();
        }
        if (request.ProfileImage is not null)
        {
            if (ImageRules.Check(request.ProfileImage) is { } imageError)
            {
                throw new BadHttpRequestException(imageError);
            }
            user.ProfileImage = request.ProfileImage.Trim();
        }
        if (request.Address is not null)
        {
            await SetMainAddressAsync(user, request.Address);
        }

        await unitOfWork.SaveAsync();
        return await authService.ToResponseAsync(user);
    }

    // same as UpdateAsync but only for customers, returns null if not a customer
    public async Task<AuthUserResponse?> UpdateCustomerAsync(int id, UpdateProfileRequest request)
    {
        var user = await FindAsync(id);
        return user is null || user.Role.Name != AuthService.CustomerRole
            ? null
            : await UpdateAsync(id, request);
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

        // Account removal retains all linked orders, carts, and addresses.
        // Admin accounts cannot be deleted through this feature.
        if (user.Role.Name == AuthService.AdminRole)
        {
            throw new BadHttpRequestException("Admin accounts cannot be deleted.");
        }

        user.IsDeleted = true;
        await unitOfWork.SaveAsync();
        return true;
    }

    private Task<User?> FindAsync(int id) =>
        repository.FindAsync(id);

    // the app only has one address text box, so we keep it as one line and leave city empty.
    // an empty text removes the address
    private async Task SetMainAddressAsync(User user, string text)
    {
        text = text.Trim();
        var main = await repository.DefaultAddressAsync(user.Id);

        if (text == "")
        {
            if (main is not null)
            {
                main.IsDeleted = true;
            }
        }
        else if (main is null)
        {
            repository.AddAddress(new Address { User = user, AddressLine1 = text, City = "", IsDefault = true });
        }
        else
        {
            main.AddressLine1 = text;
        }
    }
}
