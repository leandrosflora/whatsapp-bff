using System.Net;
using System.Security.Cryptography;
using System.Text;
using Confluent.Kafka;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Tests.Adapters.Outbound.Messaging;
using Xunit;

namespace whatsapp_bff.Tests.Adapters.Inbound.Http;

public class WhatsAppWebhookEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string VerifyToken = "test-verify-token";
    private const string AppSecret = "test-app-secret";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly FakeChannelEventPublisher _publisher = new();

    public WhatsAppWebhookEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WhatsApp:VerifyToken"] = VerifyToken,
                    ["WhatsApp:AppSecret"] = AppSecret
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IChannelEventPublisher>();
                services.AddSingleton<IChannelEventPublisher>(_publisher);

                // KafkaWebhookConsumerService (a hosted BackgroundService) starts for every test
                // host. Swap the real broker-backed consumer for one that just idles until
                // shutdown, so tests don't spin trying to reach a real Kafka broker.
                services.RemoveAll<IConsumer<string, string>>();
                services.AddSingleton<IConsumer<string, string>>(_ => CreateIdleConsumer());
            });
        });
    }

    [Fact]
    public async Task Verification_ValidToken_EchoesChallenge()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=challenge-123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("challenge-123", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Verification_InvalidToken_ReturnsForbidden()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            "/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=wrong-token&hub.challenge=challenge-123");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_ValidSignature_ReturnsOkAndPublishesRawWebhookToKafka()
    {
        var client = _factory.CreateClient();
        var body = BuildTextMessagePayload("wamid.unique-1");

        var response = await PostSignedAsync(client, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_publisher.RawWebhookEvents);
        Assert.Equal("5511999990000", _publisher.RawWebhookEvents.Single().PartitionKey);
    }

    [Fact]
    public async Task Webhook_KafkaPublishFails_ReturnsServiceUnavailable()
    {
        _publisher.ThrowOnRawWebhookPublish = true;
        var client = _factory.CreateClient();
        var body = BuildTextMessagePayload("wamid.unique-kafka-down");

        var response = await PostSignedAsync(client, body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_MissingSignature_ReturnsUnauthorizedAndDoesNotPublish()
    {
        var client = _factory.CreateClient();
        var body = BuildTextMessagePayload("wamid.unique-2");

        var response = await client.PostAsync(
            "/webhooks/whatsapp", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_publisher.RawWebhookEvents);
    }

    [Fact]
    public async Task Webhook_InvalidSignature_ReturnsUnauthorizedAndDoesNotPublish()
    {
        var client = _factory.CreateClient();
        var body = BuildTextMessagePayload("wamid.unique-3");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Hub-Signature-256", "sha256=" + new string('0', 64));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_publisher.RawWebhookEvents);
    }

    [Fact]
    public async Task Webhook_DuplicateMessageId_BothDeliveriesAcknowledgedButOnlyPublishedOnce()
    {
        var client = _factory.CreateClient();
        var body = BuildTextMessagePayload("wamid.duplicate-1");

        var first = await PostSignedAsync(client, body);
        var second = await PostSignedAsync(client, body);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(_publisher.RawWebhookEvents);
    }

    private static IConsumer<string, string> CreateIdleConsumer()
    {
        var mock = new Mock<IConsumer<string, string>>();
        mock.Setup(c => c.Consume(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct =>
            {
                ct.WaitHandle.WaitOne();
                throw new OperationCanceledException(ct);
            });
        return mock.Object;
    }

    private static async Task<HttpResponseMessage> PostSignedAsync(HttpClient client, string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        var signature = "sha256=" + Convert.ToHexString(hmac.ComputeHash(bodyBytes));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Hub-Signature-256", signature);

        return await client.SendAsync(request);
    }

    private static string BuildTextMessagePayload(string messageId) => $$"""
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "id": "entry-1",
        "changes": [{
          "field": "messages",
          "value": {
            "messages": [{
              "id": "{{messageId}}",
              "from": "5511999990000",
              "timestamp": "1700000000",
              "type": "text",
              "text": { "body": "hello" }
            }]
          }
        }]
      }]
    }
    """;
}
