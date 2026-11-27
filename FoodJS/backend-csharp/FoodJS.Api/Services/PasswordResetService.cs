using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using FoodJS.Api.Common;
using FoodJS.Api.Contracts;
using FoodJS.Api.Models;
using FoodJS.Api.Repositories;

namespace FoodJS.Api.Services;

// Account recovery rules only; all persistence goes through repositories.
public sealed class PasswordResetService(IUserRepository repository, IUnitOfWorkRepository unitOfWork,
    IEmailService emailService, IConfiguration config, IWebHostEnvironment environment, TimeProvider clock,
    ILogger<PasswordResetService> logger)
{
    private const string RequestMessage = "If this email belongs to an eligible account and delivery is available, check your inbox and spam folder for a password-reset link. Wait at least 60 seconds before requesting another link.";

    private Uri FrontendUri()
    {
        // Never trust the incoming Host header or a caller-provided redirect URL.
        if (!Uri.TryCreate(config["Email:FrontendUrl"], UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback &&
                (environment.IsDevelopment() || environment.IsEnvironment("Testing")))) ||
            uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "")
            throw new ApiException(503, "A trusted frontend URL must be configured for password-reset emails.");
        return uri;
    }

    public async Task<ForgotPasswordResponse> RequestAsync(string? email)
    {
        email = email?.Trim();
        if (email is null || email.Length > 254 || !MailAddress.TryCreate(email, out _))
            throw new ApiException(400, "Enter a valid email address.");
        var frontend = FrontendUri();
        // Configuration failures have the same response regardless of account existence.
        emailService.EnsureConfigured();
        PasswordResetToken record;
        Uri link;
        await using (var transaction = await unitOfWork.BeginTransactionAsync())
        {
            var user = await repository.FindByEmailAsync(email);
            if (user is null || !user.IsEmailVerified || string.IsNullOrEmpty(user.PasswordHash))
                return new(RequestMessage);
            var now = clock.GetUtcNow();
            var latest = await repository.LatestResetTokenAsync(user.Id);
            if (latest is not null && latest.CreatedAt.AddSeconds(60) > now) return new(RequestMessage);
            foreach (var old in await repository.UnusedResetTokensAsync(user.Id)) old.UsedAt = now;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            record = new PasswordResetToken
            {
                UserId = user.Id, TokenHash = Hash(user.Email, token), CreatedAt = now, ExpiresAt = now.AddMinutes(30)
            };
            repository.AddResetToken(record);
            // Fragments are not sent in HTTP access logs/referrers; React scrubs them on load.
            link = new Uri(frontend, $"reset-password#email={Uri.EscapeDataString(user.Email)}&token={token}");
            await unitOfWork.SaveAsync();
            await transaction.CommitAsync();
        }
        try { await emailService.SendPasswordResetAsync(email, link); }
        catch (ApiException failure) when (failure.StatusCode == 503)
        {
            // A delivery failure must not reveal whether an account exists or leak token URLs.
            record.UsedAt = clock.GetUtcNow();
            await unitOfWork.SaveAsync();
            logger.LogWarning("A password-reset email could not be delivered. The reset link was invalidated.");
        }
        return new(RequestMessage); // Never return a raw reset token, even in Development/Testing.
    }

    public async Task<bool> ResetAsync(string? email, string? token, string? newPassword)
    {
        email = email?.Trim();
        if (email is null || email.Length > 254 || !MailAddress.TryCreate(email, out _) ||
            token is null || token.Length != 64 || !token.All(Uri.IsHexDigit)) return false;
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6 || newPassword.Length > 128)
            throw new ApiException(400, "Password must be between 6 and 128 characters.");
        await using (var transaction = await unitOfWork.BeginTransactionAsync())
        {
            var user = await repository.FindByEmailAsync(email);
            if (user is null || !user.IsEmailVerified || string.IsNullOrEmpty(user.PasswordHash)) return false;
            var record = await repository.FindResetTokenAsync(user.Id, Hash(user.Email, token));
            var now = clock.GetUtcNow();
            if (record is null || record.ExpiresAt <= now) return false;
            user.PasswordHash = AuthService.Hasher.HashPassword(user, newPassword);
            user.AuthVersion++; // Existing login tokens stop working immediately.
            foreach (var old in await repository.UnusedResetTokensAsync(user.Id)) old.UsedAt = now;
            await unitOfWork.SaveAsync();
            await transaction.CommitAsync();
        }
        // The reset already succeeded; notification outages must not tell the user it failed.
        try { await emailService.SendPasswordChangedAsync(email); }
        catch (ApiException failure) when (failure.StatusCode == 503)
        { logger.LogWarning("A password-change notification could not be delivered."); }
        return true;
    }

    // Bind the token to the email as well as the user: changing email invalidates old links.
    private static string Hash(string email, string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.ToUpperInvariant() + "\n" + token)));
}
