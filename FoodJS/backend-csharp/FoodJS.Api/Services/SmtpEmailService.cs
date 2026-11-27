using System.Net;
using System.Net.Mail;
using FoodJS.Api.Common;

namespace FoodJS.Api.Services;

// SMTP transport only. Account rules belong to the verification/recovery services.
public sealed class SmtpEmailService(IConfiguration config) : IEmailService
{
    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(config["Email:Smtp:Host"]) ||
            !MailAddress.TryCreate(config["Email:FromAddress"], out _) ||
            !int.TryParse(config["Email:Smtp:Port"], out var port) || port is < 1 or > 65535 || port == 465 ||
            string.IsNullOrWhiteSpace(config["Email:Smtp:Username"]) ||
            string.IsNullOrWhiteSpace(config["Email:Smtp:Password"]))
            throw new ApiException(503, "Email delivery is not configured. Ask the administrator to configure SMTP with STARTTLS (usually port 587).");
    }

    public async Task SendVerificationAsync(string recipient, Uri verificationLink, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var message = VerificationEmailTemplate.CreateMessage(config["Email:FromAddress"]!, recipient, verificationLink);
        await SendAsync(message, "The verification email could not be sent. Please try resending it later.", ct);
    }

    public async Task SendPasswordResetAsync(string recipient, Uri resetLink, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var message = PasswordResetEmailTemplate.CreateMessage(config["Email:FromAddress"]!, recipient, resetLink);
        await SendAsync(message, "The password-reset email could not be sent. Please try again later.", ct);
    }

    public async Task SendPasswordChangedAsync(string recipient, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var message = PasswordResetEmailTemplate.CreateChangedMessage(config["Email:FromAddress"]!, recipient);
        await SendAsync(message, "The password-change notification could not be sent.", ct);
    }

    private async Task SendAsync(MailMessage message, string failureMessage, CancellationToken ct)
    {
        using var client = new SmtpClient(config["Email:Smtp:Host"]!, int.Parse(config["Email:Smtp:Port"]!))
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(config["Email:Smtp:Username"], config["Email:Smtp:Password"])
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try { await client.SendMailAsync(message, timeout.Token); }
        catch (Exception error) when (error is SmtpException || (error is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // Do not expose SMTP credentials, recipients or token URLs in errors/logs.
            throw new ApiException(503, failureMessage);
        }
    }
}
