using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;

namespace Ohrbob.Api.Tests.ApiTests;

public class StripeControllerTests
{
    private const string TestApiKey = "123456";
    private const string CheckoutUrl = "https://checkout.stripe.test/session";
    private const string PortalUrl = "https://billing.stripe.test/session";

    private ApiTestApp<Program> _app = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void Setup()
    {
        _app = new ApiTestApp<Program>(services =>
        {
            services.AddSingleton<IStripeService>(new FakeStripeService());
        }, configuration =>
        {
            var config = new
            {
                MongoDbLogs = new MongoDbSettings { ConnectionString = "mongodb://localhost:27017/TestsStripeLogs", DatabaseName = "TestsStripeLogs" },
                MongoDbData = new MongoDbSettings { ConnectionString = "mongodb://localhost:27017/TestsStripeData", DatabaseName = "TestsStripeData" },
                ApiKeys = new[] { new { Key = TestApiKey, Name = "test", Scopes = new[] { "all" } } }
            };
            configuration.AddJsonStream(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(config)));
        }, setupMongoDbContext: true);

        _client = _app.CreateDefaultClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _app.Dispose();
    }

    [Test]
    public async Task Checkout_WithoutApiKey_IsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/stripe/checkout", Request());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Checkout_KnownAccount_ReturnsCheckoutUrl()
    {
        var account = await CreateAccountAsync();

        var response = await PostAuthorizedAsync("/api/stripe/checkout", Request(account.Id));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var session = await response.Content.ReadFromJsonAsync<StripeSessionResponse>();
        Assert.That(session!.Url, Is.EqualTo(CheckoutUrl));
    }

    [Test]
    public async Task Checkout_UnknownAccount_ReturnsNotFound()
    {
        var response = await PostAuthorizedAsync("/api/stripe/checkout", Request("missing"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Portal_KnownAccount_ReturnsPortalUrl()
    {
        var account = await CreateAccountAsync();

        var response = await PostAuthorizedAsync("/api/stripe/portal", new StripePortalRequest
        {
            AccountId = account.Id,
            ReturnUrl = "https://app.test/Account"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var session = await response.Content.ReadFromJsonAsync<StripeSessionResponse>();
        Assert.That(session!.Url, Is.EqualTo(PortalUrl));
    }

    [Test]
    public async Task Webhook_WithoutSignature_IsBadRequest()
    {
        var response = await _client.PostAsync("/api/stripe/webhook", new StringContent("{}"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private static StripeCheckoutRequest Request(string accountId = "a1") => new()
    {
        AccountId = accountId,
        SuccessUrl = "https://app.test/Account?checkout=success",
        CancelUrl = "https://app.test/Account?checkout=cancelled"
    };

    private async Task<HttpResponseMessage> PostAuthorizedAsync(string url, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-API-KEY", TestApiKey);
        request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<Account> CreateAccountAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/create");
        request.Headers.Add("X-API-KEY", TestApiKey);
        request.Content = JsonContent.Create(new CreateAccountRequest
        {
            Email = $"stripe-{Guid.NewGuid()}@test.com",
            Name = "Stripe User",
            IdentityProvider = new IdpUser { Issuer = "https://test.example.com", UserId = Guid.NewGuid().ToString() }
        });

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Account>())!;
    }

    private sealed class FakeStripeService : IStripeService
    {
        public bool IsConfigured => true;

        public Task<string?> CreateCheckoutSessionAsync(Account account, string successUrl, string cancelUrl) =>
            Task.FromResult<string?>(CheckoutUrl);

        public Task<string?> CreatePortalSessionAsync(Account account, string returnUrl) =>
            Task.FromResult<string?>(PortalUrl);

        public Task HandleWebhookAsync(string json, string signature) => Task.CompletedTask;
    }
}
