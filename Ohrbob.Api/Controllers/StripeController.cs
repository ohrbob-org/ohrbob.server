using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Requests;
using Stripe;
using AccountService = Ohrbob.Api.Services.AccountService;

namespace Ohrbob.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StripeController : ControllerBase
{
    private readonly IStripeService _stripe;
    private readonly AccountService _accounts;
    private readonly ILogger<StripeController> _logger;

    public StripeController(IStripeService stripe, AccountService accounts, ILogger<StripeController> logger)
    {
        _stripe = stripe;
        _accounts = accounts;
        _logger = logger;
    }

    [EndpointSummary("Start a Stripe Checkout session for Morebob")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpPost("checkout")]
    public async Task<ActionResult<StripeSessionResponse>> Checkout(StripeCheckoutRequest request)
    {
        if (!_stripe.IsConfigured)
            return Problem("Billing is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var account = await _accounts.GetAsync(request.AccountId);
        if (account == null) return NotFound();

        var url = await _stripe.CreateCheckoutSessionAsync(account, request.SuccessUrl, request.CancelUrl);
        if (url == null)
            return Problem("Billing is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

        return Ok(new StripeSessionResponse { Url = url });
    }

    [EndpointSummary("Open the Stripe billing portal for an account")]
    [Authorize(Policy = Consts.GetAccountPolicy)]
    [HttpPost("portal")]
    public async Task<ActionResult<StripeSessionResponse>> Portal(StripePortalRequest request)
    {
        if (!_stripe.IsConfigured)
            return Problem("Billing is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var account = await _accounts.GetAsync(request.AccountId);
        if (account == null) return NotFound();

        var url = await _stripe.CreatePortalSessionAsync(account, request.ReturnUrl);
        if (url == null)
            return Problem("This account has no billing history yet.", statusCode: StatusCodes.Status400BadRequest);

        return Ok(new StripeSessionResponse { Url = url });
    }

    [EndpointSummary("Stripe webhook endpoint")]
    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var signature = Request.Headers["Stripe-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(signature)) return BadRequest();

        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync();

        try
        {
            await _stripe.HandleWebhookAsync(json, signature);
            return Ok();
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Rejected Stripe webhook.");
            return BadRequest();
        }
        catch (InvalidOperationException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
