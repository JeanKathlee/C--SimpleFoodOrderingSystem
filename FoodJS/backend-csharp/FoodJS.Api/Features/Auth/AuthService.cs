using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FoodJS.Api.Data;
using FoodJS.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FoodJS.Api.Features.Auth;

public sealed class AuthService(FoodJsDbContext db, IConfiguration config, IHttpClientFactory httpClientFactory)
{
    public const string AdminRole = "admin";
    public const string CustomerRole = "customer";

    // use this same hasher when saving new passwords (like on register), or login won't match
    public static readonly PasswordHasher<User> Hasher = new();

    public static SymmetricSecurityKey SigningKey(IConfiguration config) =>
        new(Encoding.UTF8.GetBytes(config["Jwt:Secret"]!));

    public async Task<AuthResponse?> LoginAsync(string email, string password)
    {
        var user = await FindByEmailAsync(email);

        // google users have no password, so they can't log in with one
        if (user is null || user.PasswordHash == "" ||
            Hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            return null;
        }

        return new AuthResponse("Login successful.", CreateToken(user), ToResponse(user));
    }

    // if something goes wrong this throws an error with a message, the controller sends it back
    public async Task<AuthResponse> GoogleLoginAsync(string code, string redirectUri)
    {
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
            user = new User
            {
                FirstName = googleUser.GivenName ?? "Google",
                LastName = googleUser.FamilyName ?? "User",
                Email = googleUser.Email,
                PasswordHash = "",
                Role = await db.Roles.SingleAsync(r => r.Name == CustomerRole),
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        return new AuthResponse("Login successful.", CreateToken(user), ToResponse(user));
    }

    // only runs on our own computers: makes the tables, the roles, and 2 demo users
    public static async Task SeedDevelopmentDataAsync(FoodJsDbContext db)
    {
        await db.Database.MigrateAsync();

        foreach (var name in new[] { AdminRole, CustomerRole })
        {
            if (!await db.Roles.AnyAsync(r => r.Name == name))
            {
                db.Roles.Add(new Role { Name = name });
            }
        }
        await db.SaveChangesAsync();

        await AddDemoUserAsync(db, "admin@foodjs.demo", "admin123", "Administrator", AdminRole);
        await AddDemoUserAsync(db, "customer@foodjs.demo", "customer123", "Customer", CustomerRole);
    }

    private static async Task AddDemoUserAsync(FoodJsDbContext db, string email, string password, string lastName, string role)
    {
        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            return;
        }

        var user = new User
        {
            FirstName = "Demo",
            LastName = lastName,
            Email = email,
            PasswordHash = "",
            Role = await db.Roles.SingleAsync(r => r.Name == role),
        };
        user.PasswordHash = Hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    private Task<User?> FindByEmailAsync(string email) =>
        db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

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

    private static AuthUserResponse ToResponse(User user) => new(
        user.Id,
        user.FirstName,
        user.LastName,
        $"{user.FirstName} {user.LastName}".Trim(),
        user.Email,
        user.Role.Name,
        user.MobileNumber ?? "",
        "",
        [],
        user.ProfileImage ?? "",
        "",
        "",
        user.CreatedAt);
}

public static class ClaimsPrincipalExtensions
{
    // gets the id of whoever is logged in
    public static int GetUserId(this ClaimsPrincipal user) => int.Parse(user.FindFirstValue("sub")!);
}
