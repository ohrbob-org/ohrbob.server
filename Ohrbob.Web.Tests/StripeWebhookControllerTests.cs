using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Controllers;

namespace Ohrbob.Web.Tests;

public class StripeWebhookControllerTests
{
    [Test]
    public async Task Webhook_ForwardsRawBodyAndSignature()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var controller = CreateController(handler);

        var result = await controller.Webhook() as ObjectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(200));
        Assert.That(handler.Request!.RequestUri!.ToString(), Is.EqualTo("http://api.test/api/stripe/webhook"));
        Assert.That(handler.Request.Headers.GetValues("X-API-KEY").Single(), Is.EqualTo("key"));
        Assert.That(handler.Request.Headers.GetValues("Stripe-Signature").Single(), Is.EqualTo("t=1,v1=abc"));
        Assert.That(handler.Body, Is.EqualTo("{\"id\":\"evt_1\"}"));
    }

    [Test]
    public async Task Webhook_WithoutSignature_IsBadRequest()
    {
        var controller = CreateController(new RecordingHandler(HttpStatusCode.OK), signature: null);

        var result = await controller.Webhook();

        Assert.That(result, Is.InstanceOf<BadRequestResult>());
    }

    [Test]
    public async Task Webhook_WhenApiIsUnavailable_ReturnsServiceUnavailable()
    {
        var controller = CreateController(new RecordingHandler(new HttpRequestException("boom")));

        var result = await controller.Webhook() as StatusCodeResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(503));
    }

    private static StripeWebhookController CreateController(RecordingHandler handler, string? signature = "t=1,v1=abc")
    {
        var factory = new FakeClientFactory(new HttpClient(handler));
        var options = Options.Create(new OhrbobApiOptions
        {
            BaseUrl = "http://api.test/api/",
            ApiKey = "key"
        });
        var controller = new StripeWebhookController(
            factory,
            options,
            NullLogger<StripeWebhookController>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":\"evt_1\"}"));
        if (signature != null) context.Request.Headers["Stripe-Signature"] = signature;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private sealed class FakeClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public FakeClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _status;
        private readonly Exception? _exception;

        public RecordingHandler(HttpStatusCode status) => _status = status;
        public RecordingHandler(Exception exception) => _exception = exception;

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (_exception != null) throw _exception;
            return new HttpResponseMessage(_status!.Value) { Content = new StringContent("") };
        }
    }
}
