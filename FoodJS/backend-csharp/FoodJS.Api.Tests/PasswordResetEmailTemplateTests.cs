using System.Net;
using FoodJS.Api.Services;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class PasswordResetEmailTemplateTests
{
    [Fact]
    public void Reset_email_has_a_button_plain_text_fallback_and_expiry_notice()
    {
        var link = new Uri("https://foodjs.example.test/reset-password#email=qa%40example.test&token=" + new string('A', 64));
        using var message = PasswordResetEmailTemplate.CreateMessage("sender@example.test", "qa@example.test", link);
        Assert.Equal("Reset your FoodJS password", message.Subject);
        Assert.Contains(link.AbsoluteUri, message.Body); Assert.Contains("30 minutes", message.Body);
        Assert.Contains("only be used once", message.Body); Assert.False(message.IsBodyHtml);
        using var reader = new StreamReader(Assert.Single(message.AlternateViews).ContentStream);
        var html = reader.ReadToEnd();
        Assert.Contains(">Reset password</a>", html);
        Assert.Contains($"href=\"{WebUtility.HtmlEncode(link.AbsoluteUri)}\"", html);
        Assert.Contains("Your password has not changed", html);
    }

    [Fact]
    public void Changed_notification_never_contains_a_password_or_reset_token()
    {
        using var message = PasswordResetEmailTemplate.CreateChangedMessage("sender@example.test", "qa@example.test");
        Assert.Contains("was changed", message.Subject);
        Assert.Contains("sessions have been revoked", message.Body);
        Assert.DoesNotContain("token=", message.Body);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///tmp/reset")]
    public void Unsafe_button_urls_are_rejected(string url) =>
        Assert.Throws<ArgumentException>(() => PasswordResetEmailTemplate.CreateMessage("sender@example.test", "qa@example.test", new Uri(url)));
}
