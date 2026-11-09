using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Controllers;

/// <summary>
/// Public Stripe webhook entry point. The API is only reachable inside the
/// Compose network, so the Web forwards the raw payload and signature to it.
/// </summary>
[AllowAnonymous]
[Route("stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly OhrbobApiOptions _apiOptions;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        IHttpClientFactory clientFactory,
        IOptions<OhrbobApiOptions> apiOptions,
        ILogger<StripeWebhookController> logger)
    {
        _clientFactory = clientFactory;
        _apiOptions = apiOptions.Value;
        _apiOptions.Initialize();
        _logger = logger;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var signature = Request.Headers["Stripe-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(signature)) return BadRequest();

        using var body = new MemoryStream();
        await Request.Body.CopyToAsync(body, HttpContext.RequestAborted);
        body.Position = 0;

        using var forward = new HttpRequestMessage(HttpMethod.Post, _apiOptions.BaseUrl + "/stripe/webhook");
        forward.Headers.TryAddWithoutValidation("X-API-KEY", _apiOptions.ApiKey);
        forward.Headers.TryAddWithoutValidation("Stripe-Signature", signature);
        forward.Content = new StreamContent(body);
        forward.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        try
        {
            using var client = _clientFactory.CreateClient(nameof(OhrbobApiService));
            using var response = await client.SendAsync(forward, HttpContext.RequestAborted);
            var responseBody = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            return StatusCode((int)response.StatusCode, responseBody);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Failed to forward Stripe webhook to the API.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
