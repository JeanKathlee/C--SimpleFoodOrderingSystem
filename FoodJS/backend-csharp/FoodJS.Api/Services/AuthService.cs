using FoodJS.Api.Contracts;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FoodJS.Api.Repositories;
using FoodJS.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FoodJS.Api.Services;

public sealed class AuthService(
    IUserRepository repository, IUnitOfWorkRepository unitOfWork,
    IConfiguration config,
    IHttpClientFactory httpClientFactory,
    IWebHostEnvironment environment)
{
    public const string AdminRole = "admin";
    public const string CustomerRole = "customer";

    // use this same hasher when saving new passwords (like on register) or login won't match
    public static readonly PasswordHasher<User> Hasher = new();

    public static SymmetricSecurityKey SigningKey(IConfiguration config) =>
        new(Encoding.UTF8.GetBytes(config["Jwt:Secret"]!));

    public async Task<AuthResponse?> LoginAsync(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            throw new BadHttpRequestException("Email and password are required.");
        var user = await FindByEmailAsync(email.Trim());

        // google users have no password, so they can't log in with one
        if (user is null || user.PasswordHash == "" ||
            Hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            return null;
        }

        return await CreateSessionAsync(user, "Login successful.");
    }

    public async Task<ForgotPasswordResponse> CreatePasswordResetAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new BadHttpRequestException("Email is required.");
        email = email.Trim();
        const string message = "If that email is registered, password-reset instructions are ready.";
        var user = await FindByEmailAsync(email);
        if (user is null)
        {
            return new ForgotPasswordResponse(message);
        }

        var previousTokens = await repository.UnusedResetTokensAsync(user.Id);
        foreach (var previous in previousTokens)
        {
            previous.UsedAt = DateTimeOffset.UtcNow;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        repository.AddResetToken(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = HashResetToken(token),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
        });
        await unitOfWork.SaveAsync();

        // The classroom/local build has no mail provider. Expose the one-time token
        // only in Development so the complete reset flow can be demonstrated.
        return new ForgotPasswordResponse(
            message,
            environment.IsDevelopment() || environment.IsEnvironment("Testing") ? token : null);
    }

    public async Task<bool> ResetPasswordAsync(string? email, string? token, string? newPassword)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(newPassword))
            throw new BadHttpRequestException("Email, reset token, and new password are required.");
        email = email.Trim();
        token = token.Trim();
        if (newPassword.Length < 6)
        {
            throw new BadHttpRequestException("Password must be at least 6 characters.");
        }

        var user = await FindByEmailAsync(email);
        if (user is null)
        {
            return false;
        }

        var tokenHash = HashResetToken(token);
        var reset = await repository.FindResetTokenAsync(user.Id, tokenHash);
        if (reset is null || reset.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        user.PasswordHash = Hasher.HashPassword(user, newPassword);
        reset.UsedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveAsync();
        return true;
    }

    private static string HashResetToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // if something goes wrong this throws an error with a message, the controller sends it back
    public async Task<AuthResponse> GoogleLoginAsync(string? code, string? redirectUri)
    {
        if (string.IsNullOrEmpty(code)) throw new BadHttpRequestException("Authorization code is required");
        var configuredRedirectUri = config["Google:RedirectUri"];
        if (string.IsNullOrWhiteSpace(configuredRedirectUri))
            throw new BadHttpRequestException("Google login is not configured on the server.", 500);
        redirectUri ??= configuredRedirectUri;
        if (!string.Equals(redirectUri, configuredRedirectUri, StringComparison.Ordinal))
            throw new BadHttpRequestException("Invalid Google redirect URI.");
        var clientSecret = config["Google:ClientSecret"];
        if (string.IsNullOrEmpty(clientSecret))
        {
            throw new BadHttpRequestException("Google login is not configured on the server.", 500);
        }

        var http = httpClientFactory.CreateClient();

        var tokenResponse = await http.PostAsync("https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = config["Google:ClientId"]!,
                ["client_secret"] = clientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
            }));
        if (!tokenResponse.IsSuccessStatusCode)
        {
            throw new BadHttpRequestException("Invalid authorization code", 401);
        }

        var accessToken = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("access_token").GetString();

        using var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
        userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var userInfoResponse = await http.SendAsync(userInfoRequest);
        if (!userInfoResponse.IsSuccessStatusCode)
        {
            throw new BadHttpRequestException("Failed to get user info from Google", 401);
        }

        var googleUser = await userInfoResponse.Content.ReadFromJsonAsync<GoogleUserInfo>();
        if (string.IsNullOrEmpty(googleUser?.Email))
        {
            throw new BadHttpRequestException("Email not provided by Google", 400);
        }

        // only trust emails google has checked, or someone could log in as another user
        if (!googleUser.VerifiedEmail)
        {
            throw new BadHttpRequestException("Google email is not verified", 400);
        }

        var user = await FindByEmailAsync(googleUser.Email);
        if (user is null)
        {
            if (await repository.EmailExistsAsync(googleUser.Email, 0))
                throw new BadHttpRequestException("This account is unavailable.", 401);
            user = new User
            {
                FirstName = googleUser.GivenName ?? "Google",
                LastName = googleUser.FamilyName ?? "User",
                Email = googleUser.Email,
                PasswordHash = "",
                Role = await repository.GetRoleAsync(CustomerRole),
            };
            repository.Add(user);
            await unitOfWork.SaveAsync();
        }

        return await CreateSessionAsync(user, "Login successful.");
    }

    // logs the user in: gives back a new token plus the user's info
    public async Task<AuthResponse> CreateSessionAsync(User user, string message) =>
        new(message, CreateToken(user), await ToResponseAsync(user));

    public async Task EnsureRolesAsync()
    {
        var names = await repository.RoleNamesAsync();
        foreach (var name in new[] { AdminRole, CustomerRole })
        {
            if (!names.Contains(name))
            {
                repository.AddRole(new Role { Name = name });
            }
        }
        await unitOfWork.SaveAsync();
    }

    // Only runs on developer machines: creates the two documented demo accounts.
    public async Task SeedDevelopmentDataAsync()
    {
        await AddDemoUserAsync("admin@foodjs.demo", "admin123", "Administrator", AdminRole);
        await AddDemoUserAsync("customer@foodjs.demo", "customer123", "Customer", CustomerRole);
    }

    public async Task SeedConfiguredAdminAsync(
        string? email,
        string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        if (password.Length < 12)
        {
            throw new InvalidOperationException("BootstrapAdmin:Password must contain at least 12 characters.");
        }

        await AddDemoUserAsync(email.Trim(), password, "Administrator", AdminRole);
    }

    private async Task AddDemoUserAsync(string email, string password, string lastName, string role)
    {
        if (await repository.EmailExistsAsync(email, 0))
        {
            return;
        }

        var user = new User
        {
            FirstName = "Demo",
            LastName = lastName,
            Email = email,
            PasswordHash = "",
            Role = await repository.GetRoleAsync(role),
        };
        user.PasswordHash = Hasher.HashPassword(user, password);
        repository.Add(user);
        await unitOfWork.SaveAsync();
    }

    public Task<User?> FindByEmailAsync(string email) =>
        repository.FindByEmailAsync(email);

    public async Task<bool> IsTokenAccountActiveAsync(int userId, string? tokenRole)
    {
        var user = await repository.FindAsync(userId);
        return user is not null && user.Role.Name == tokenRole;
    }

    // makes the login token, it holds the user id, email, and role
    private string CreateToken(User user) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Claims = new Dictionary<string, object>
        {
            ["sub"] = user.Id.ToString(),
            ["email"] = user.Email,
            ["role"] = user.Role.Name,
        },
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(SigningKey(config), SecurityAlgorithms.HmacSha256),
    });

    // the user info we send to the react app (never the password)
    // user.Role must be loaded first
    public async Task<AuthUserResponse> ToResponseAsync(User user)
    {
        // main address first, the rest after it
        var addresses = await repository.AddressLinesAsync(user.Id);

        return new(
            user.Id,
            user.FirstName,
            user.LastName,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.Email,
            user.Role.Name,
            user.MobileNumber ?? "",
            addresses.FirstOrDefault() ?? "",
            addresses,
            user.ProfileImage ?? "",
            "",
            "",
            user.CreatedAt);
    }
}
