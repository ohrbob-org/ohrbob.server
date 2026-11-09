using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Ohrbob.Api.Configuration;

namespace Ohrbob.Api.Tests.ApiTests;

public class StripeOptionsTests
{
    [Test]
    public void FromConfiguration_ReadsEnvStyleNames()
    {
        var values = new Dictionary<string, string?>
        {
            ["Stripe:SECRET_KEY"] = "sk_test_123",
            ["Stripe:PUBLIC_KEY"] = "pk_test_123",
            ["Stripe:WEBHOOK_SECRET"] = "whsec_123",
            ["Stripe:MOREBOB_PRICE_ID"] = "price_123"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var options = StripeOptions.FromConfiguration(configuration);

        Assert.That(options.SecretKey, Is.EqualTo("sk_test_123"));
        Assert.That(options.PublicKey, Is.EqualTo("pk_test_123"));
        Assert.That(options.WebhookSecret, Is.EqualTo("whsec_123"));
        Assert.That(options.MorebobPriceId, Is.EqualTo("price_123"));
        Assert.That(options.IsConfigured, Is.True);
    }

    [Test]
    public void FromConfiguration_ReadsCamelCaseNames()
    {
        var values = new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "sk_test_123",
            ["Stripe:WebhookSecret"] = "whsec_123",
            ["Stripe:MorebobPriceId"] = "price_123"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var options = StripeOptions.FromConfiguration(configuration);

        Assert.That(options.SecretKey, Is.EqualTo("sk_test_123"));
        Assert.That(options.IsConfigured, Is.True);
    }

    [Test]
    public void IsConfigured_IsFalseWithoutPriceAndWebhookSecret()
    {
        var options = new StripeOptions { SecretKey = "sk_test_123" };

        Assert.That(options.IsConfigured, Is.False);
    }
}
