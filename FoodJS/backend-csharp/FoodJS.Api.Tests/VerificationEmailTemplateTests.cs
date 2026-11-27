using System.Net;
using System.Net.Mime;
using FoodJS.Api.Services;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class VerificationEmailTemplateTests
{
    [Fact]
    public void Email_has_an_html_confirmation_button_and_a_plain_text_fallback()
    {
        var link = new Uri("https://foodjs.example.test/verify-email#email=buyer%40example.test&token=" + new string('A', 64));
        using var message = VerificationEmailTemplate.CreateMessage("sender@example.test", "buyer@example.test", link);
        Assert.Equal("Confirm your FoodJS email", message.Subject);
        Assert.Contains(link.AbsoluteUri, message.Body);
        Assert.Contains("30 minutes", message.Body);
        Assert.False(message.IsBodyHtml);
        var view = Assert.Single(message.AlternateViews);
        Assert.Equal(MediaTypeNames.Text.Html, view.ContentType.MediaType);
        using var reader = new StreamReader(view.ContentStream);
        var html = reader.ReadToEnd();
        Assert.Contains(">Confirm email</a>", html);
        Assert.Contains($"href=\"{WebUtility.HtmlEncode(link.AbsoluteUri)}\"", html);
        Assert.Contains("&amp;token=", html);
        Assert.Contains("padding:16px 30px", html);
        Assert.Contains("confirmation page", html);
        Assert.DoesNotContain("Local testing:", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void Local_emails_explain_that_the_link_must_be_opened_on_the_development_computer()
    {
        using var message = VerificationEmailTemplate.CreateMessage("sender@example.test", "buyer@example.test",
            new Uri("http://localhost:5173/verify-email#email=buyer%40example.test&token=" + new string('A', 64)));
        Assert.Contains("computer running FoodJS", message.Body);
        using var reader = new StreamReader(Assert.Single(message.AlternateViews).ContentStream);
        Assert.Contains("will not work on a different phone", reader.ReadToEnd());
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///tmp/verification.html")]
    public void Email_button_rejects_non_web_urls(string url)
    {
        Assert.Throws<ArgumentException>(() => VerificationEmailTemplate.CreateMessage("sender@example.test", "buyer@example.test", new Uri(url)));
    }
}
