using Ohrbob.Api.Configuration;
using Ohrbob.Api.Extensions.Logger;
using Ohrbob.Server.Shared.Data;
using Stripe;
using Stripe.Checkout;
using BillingPortalSessionService = Stripe.BillingPortal.SessionService;
using Account = Ohrbob.Server.Shared.Data.Account;

namespace Ohrbob.Api.Services;

public class StripeService : IStripeService
{
    private readonly StripeOptions _options;
    private readonly AccountService _accounts;
    private readonly ILogger<StripeService> _logger;
    private readonly IStripeClient _client;

    public StripeService(StripeOptions options, AccountService accounts, ILogger<StripeService> logger)
    {
        _options = options;
        _accounts = accounts;
        _logger = logger;
        _client = new StripeClient(string.IsNullOrWhiteSpace(options.SecretKey) ? null : options.SecretKey);
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<string?> CreateCheckoutSessionAsync(Account account, string successUrl, string cancelUrl)
    {
        if (!IsConfigured) return null;

        var customerId = account.StripeCustomerId;
        if (string.IsNullOrWhiteSpace(customerId))
        {
            var customer = await new CustomerService(_client).CreateAsync(new CustomerCreateOptions
            {
                Email = account.Email,
                Name = account.Name,
                Metadata = new Dictionary<string, string> { ["accountId"] = account.Id }
            });
            customerId = customer.Id;
            account.StripeCustomerId = customerId;
            await _accounts.UpdateAsync(account);
        }

        var session = await new SessionService(_client).CreateAsync(new SessionCreateOptions
        {
            Mode = "subscription",
            Customer = customerId,
            ClientReferenceId = account.Id,
            LineItems = [new SessionLineItemOptions { Price = _options.MorebobPriceId, Quantity = 1 }],
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string> { ["accountId"] = account.Id }
            }
        });

        _logger.LogAudit("StripeCheckoutStarted", account.Id, new { customerId, sessionId = session.Id });
        return session.Url;
    }

    public async Task<string?> CreatePortalSessionAsync(Account account, string returnUrl)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(account.StripeCustomerId)) return null;

        var session = await new BillingPortalSessionService(_client).CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = account.StripeCustomerId,
            ReturnUrl = returnUrl
        });

        _logger.LogAudit("StripePortalOpened", account.Id, new { customerId = account.StripeCustomerId });
        return session.Url;
    }

    public async Task HandleWebhookAsync(string json, string signature)
    {
        if (!IsConfigured) throw new InvalidOperationException("Stripe is not configured.");

        var stripeEvent = EventUtility.ConstructEvent(
            json,
            signature,
            _options.WebhookSecret,
            throwOnApiVersionMismatch: false);

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                if (stripeEvent.Data.Object is Session session)
                    await SyncFromCheckoutAsync(session);
                break;
            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
                if (stripeEvent.Data.Object is Subscription subscription)
                    await SyncSubscriptionAsync(subscription);
                break;
        }
    }

    private async Task SyncFromCheckoutAsync(Session session)
    {
        var account = await FindAccountAsync(session.ClientReferenceId, session.CustomerId);
        if (account == null)
        {
            _logger.LogWarning("Stripe checkout session {SessionId} has no matching account.",
                session.Id);
            return;
        }

        if (!string.IsNullOrWhiteSpace(session.CustomerId))
            account.StripeCustomerId = session.CustomerId;

        if (string.IsNullOrWhiteSpace(session.SubscriptionId))
        {
            await _accounts.UpdateAsync(account);
            return;
        }

        var subscription = await new SubscriptionService(_client).GetAsync(session.SubscriptionId);
        await ApplySubscriptionAsync(account, subscription);
    }

    private async Task SyncSubscriptionAsync(Subscription payload)
    {
        string? accountId = null;
        payload.Metadata?.TryGetValue("accountId", out accountId);
        var account = await FindAccountAsync(accountId, payload.CustomerId);
        if (account == null)
        {
            _logger.LogWarning("Stripe subscription {SubscriptionId} has no matching account.",
                payload.Id);
            return;
        }

        // Webhooks are not ordered. Read Stripe's current state so an older "updated"
        // event cannot reactivate an already-ended subscription.
        Subscription current;
        try
        {
            current = await new SubscriptionService(_client).GetAsync(payload.Id);
        }
        catch (StripeException)
        {
            current = payload;
        }

        await ApplySubscriptionAsync(account, current);
    }

    private async Task ApplySubscriptionAsync(Account account, Subscription subscription)
    {
        var previousTier = account.Tier;
        var tier = StripeTierPolicy.ForSubscriptionStatus(subscription.Status);

        if (!string.IsNullOrWhiteSpace(subscription.CustomerId))
            account.StripeCustomerId = subscription.CustomerId;
        account.StripeSubscriptionId = subscription.Id;
        account.StripeSubscriptionStatus = subscription.Status;
        account.StripeCurrentPeriodEnd = subscription.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd;
        account.StripeCancelAt = subscription.CancelAt;
        account.StripeSubscriptionEndedAt = subscription.Status is "canceled" or "incomplete_expired"
            ? subscription.EndedAt ?? subscription.CanceledAt ?? DateTime.UtcNow
            : null;
        account.Tier = tier;
        await _accounts.UpdateAsync(account);

        _logger.LogAudit("StripeSubscriptionUpdated", account.Id, new
        {
            subscriptionId = subscription.Id,
            status = subscription.Status,
            tier = tier.ToString(),
            previousTier = previousTier.ToString(),
            cancelAt = subscription.CancelAt,
            canceledAt = subscription.CanceledAt,
            cancellationReason = subscription.CancellationDetails?.Reason
        });
    }

    private async Task<Account?> FindAccountAsync(string? accountId, string? customerId)
    {
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            var byId = await _accounts.GetAsync(accountId);
            if (byId != null) return byId;
        }

        if (!string.IsNullOrWhiteSpace(customerId))
            return await _accounts.FindByStripeCustomerIdAsync(customerId);

        return null;
    }
}
