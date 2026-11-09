using Microsoft.Extensions.Configuration;

namespace Ohrbob.Api.Configuration;

public class StripeOptions
{
    public const string ConfigSection = "Stripe";

    public string SecretKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string MorebobPriceId { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SecretKey)
        && !string.IsNullOrWhiteSpace(WebhookSecret)
        && !string.IsNullOrWhiteSpace(MorebobPriceId);

    /// <summary>
    /// Binds the section and also accepts the SCREAMING_SNAKE_CASE environment names
    /// (STRIPE__SECRET_KEY etc.) used in .env.
    /// </summary>
    public static StripeOptions FromConfiguration(IConfiguration configuration)
    {
        var options = configuration.GetSection(ConfigSection).Get<StripeOptions>() ?? new StripeOptions();
        options.SecretKey = FirstNonEmpty(options.SecretKey, configuration["Stripe:SECRET_KEY"]);
        options.PublicKey = FirstNonEmpty(options.PublicKey, configuration["Stripe:PUBLIC_KEY"]);
        options.WebhookSecret = FirstNonEmpty(options.WebhookSecret, configuration["Stripe:WEBHOOK_SECRET"]);
        options.MorebobPriceId = FirstNonEmpty(options.MorebobPriceId, configuration["Stripe:MOREBOB_PRICE_ID"]);
        return options;
    }

    private static string FirstNonEmpty(string? value, string? fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value;
}
