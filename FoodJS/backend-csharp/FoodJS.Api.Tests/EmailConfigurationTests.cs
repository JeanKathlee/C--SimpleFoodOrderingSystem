using FoodJS.Api.Common;
using FoodJS.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FoodJS.Api.Tests;

public sealed class EmailConfigurationTests
{
    private static Dictionary<string, string?> Settings() => new()
    {
        ["Email:FromAddress"] = "sender@example.test", ["Email:Smtp:Host"] = "smtp.example.test",
        ["Email:Smtp:Port"] = "587", ["Email:Smtp:Username"] = "sender@example.test", ["Email:Smtp:Password"] = "test-only"
    };

    [Fact]
    public void Configured_starttls_transport_is_accepted_without_sending_an_email()
    {
        new SmtpEmailService(new ConfigurationBuilder().AddInMemoryCollection(Settings()).Build()).EnsureConfigured();
    }

    [Theory]
    [InlineData("Email:FromAddress", "bad-address")]
    [InlineData("Email:Smtp:Host", "")]
    [InlineData("Email:Smtp:Port", "465")]
    [InlineData("Email:Smtp:Port", "0")]
    [InlineData("Email:Smtp:Port", "invalid")]
    [InlineData("Email:Smtp:Username", "")]
    [InlineData("Email:Smtp:Password", "")]
    public void Incomplete_or_incompatible_smtp_configuration_fails_clearly(string key, string? value)
    {
        var settings = Settings(); settings[key] = value;
        var service = new SmtpEmailService(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        Assert.Equal(503, Assert.Throws<ApiException>(service.EnsureConfigured).StatusCode);
    }
}
