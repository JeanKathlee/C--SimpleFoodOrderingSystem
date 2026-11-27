namespace FoodJS.Api.Services;

public interface IEmailService
{
    void EnsureConfigured();
    Task SendVerificationAsync(string recipient, Uri verificationLink, CancellationToken ct = default);
    Task SendPasswordResetAsync(string recipient, Uri resetLink, CancellationToken ct = default);
    Task SendPasswordChangedAsync(string recipient, CancellationToken ct = default);
}
