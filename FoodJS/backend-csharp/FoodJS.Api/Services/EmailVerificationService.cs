using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using FoodJS.Api.Common;
using FoodJS.Api.Contracts;
using FoodJS.Api.Models;
using FoodJS.Api.Repositories;

namespace FoodJS.Api.Services;

public sealed class EmailVerificationService(IUserRepository repository, IUnitOfWorkRepository unitOfWork,
    IEmailService emailService, IConfiguration config, IWebHostEnvironment environment, TimeProvider clock,
    ILogger<EmailVerificationService> logger)
{
    private const string ResendMessage = "If this email belongs to a pending account and delivery is available, check your inbox and spam folder for a verification link. Wait at least 60 seconds between requests.";

    public void EnsureConfigured()
    {
        _ = FrontendUri();
        emailService.EnsureConfigured();
    }

    private Uri FrontendUri()
    {
        if (!Uri.TryCreate(config["Email:FrontendUrl"], UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback &&
                (environment.IsDevelopment() || environment.IsEnvironment("Testing")))) ||
            uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "")
            throw new ApiException(503, "A trusted frontend URL must be configured for verification emails.");
        return uri;
    }

    // Caller saves/commits the user and token together, before any external email is sent.
    public async Task<Uri> PrepareAsync(User user)
    {
        var now = clock.GetUtcNow();
        foreach (var old in await repository.UnusedVerificationTokensAsync(user.Id)) old.UsedAt = now;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        repository.AddVerificationToken(new EmailVerificationToken
        {
            User = user, Email = user.Email, TokenHash = Hash(token), CreatedAt = now, ExpiresAt = now.AddMinutes(30)
        });
        // Fragment stays out of HTTP access logs and referrers. React removes it immediately.
        return new Uri(FrontendUri(), $"verify-email#email={Uri.EscapeDataString(user.Email)}&token={token}");
    }

    public async Task<bool> DeliverAsync(string email, Uri link)
    {
        try { await emailService.SendVerificationAsync(email, link); return true; }
        catch (ApiException failure) when (failure.StatusCode == 503)
        {
            logger.LogWarning("A verification email could not be delivered. The account remains pending.");
            return false;
        }
    }

    public async Task<RegistrationResponse> CompleteRegistrationAsync(User user, Uri link)
    {
        var sent = await DeliverAsync(user.Email, link);
        return new(sent ? "Account created. Check your email to verify it before signing in."
            : "Account created, but the verification email could not be sent. Use Resend verification email to try again.",
            user.Email, true, sent);
    }

    public async Task<string> ResendAsync(string? email)
    {
        email = email?.Trim();
        if (email is null || email.Length > 254 || !MailAddress.TryCreate(email, out _))
            throw new ApiException(400, "Enter a valid email address.");
        EnsureConfigured();
        Uri? link = null;
        await using (var transaction = await unitOfWork.BeginTransactionAsync())
        {
            var user = await repository.FindByEmailAsync(email);
            if (user is null || user.IsEmailVerified) return ResendMessage;
            var latest = await repository.LatestVerificationTokenAsync(user.Id);
            if (latest is not null && latest.CreatedAt.AddSeconds(60) > clock.GetUtcNow()) return ResendMessage;
            link = await PrepareAsync(user);
            await unitOfWork.SaveAsync();
            await transaction.CommitAsync();
        }
        // Same public response for absent, verified, throttled and pending accounts.
        await DeliverAsync(email, link);
        return ResendMessage;
    }

    public async Task<string> VerifyAsync(VerifyEmailRequest request)
    {
        var email = request.Email?.Trim();
        var token = request.Token;
        if (string.IsNullOrWhiteSpace(email) || token is null || token.Length != 64 || !token.All(Uri.IsHexDigit))
            throw InvalidLink();
        await using var transaction = await unitOfWork.BeginTransactionAsync();
        var record = await repository.FindVerificationTokenAsync(Hash(token));
        var now = clock.GetUtcNow();
        if (record is null || record.UsedAt is not null || record.ExpiresAt <= now ||
            !string.Equals(record.Email, email, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(record.User.Email, record.Email, StringComparison.OrdinalIgnoreCase)) throw InvalidLink();
        record.User.IsEmailVerified = true;
        foreach (var old in await repository.UnusedVerificationTokensAsync(record.UserId)) old.UsedAt = now;
        await unitOfWork.SaveAsync();
        await transaction.CommitAsync();
        return "Email verified successfully. You can now sign in.";
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static ApiException InvalidLink() => new(400, "This verification link is invalid, expired, or already used. Request a new verification email.");
}
