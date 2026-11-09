using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Services;

public interface IStripeService
{
    bool IsConfigured { get; }
    Task<string?> CreateCheckoutSessionAsync(Account account, string successUrl, string cancelUrl);
    Task<string?> CreatePortalSessionAsync(Account account, string returnUrl);
    Task HandleWebhookAsync(string json, string signature);
}
