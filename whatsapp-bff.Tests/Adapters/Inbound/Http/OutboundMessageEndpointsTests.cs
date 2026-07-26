using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Domain;
using whatsapp_bff.Tests.Testing;
using Xunit;

namespace whatsapp_bff.Tests.Adapters.Inbound.Http;

public class OutboundMessageEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public OutboundMessageEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task Send_ValidPayload_ReturnsAcceptedAndMarksTrackerSent()
    {
        var whatsAppClient = new Mock<IWhatsAppCloudApiClient>();
        whatsAppClient
            .Setup(c => c.SendTextMessageAsync("5511999990000", "hello", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppSendResult(true, "wamid.sent-1", null, null));
        var tracker = new Mock<IOutboundMessageTracker>();
        var publisher = new Mock<IChannelEventPublisher>();

        var client = CreateClient(whatsAppClient.Object, tracker.Object, publisher.Object);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem-sent-1");

        var response = await client.PostAsJsonAsync(
            "/internal/messages", new OutboundChannelMessage { To = "5511999990000", Type = "text", Text = "hello" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        tracker.Verify(t => t.MarkSent("wamid.sent-1"), Times.Once);
        publisher.Verify(
            p => p.PublishMessageStatusAsync(It.IsAny<MessageStatusEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Send_MissingRecipient_ReturnsBadRequestWithoutCallingWhatsAppApi()
    {
        var whatsAppClient = new Mock<IWhatsAppCloudApiClient>();
        var client = CreateClient(whatsAppClient.Object, Mock.Of<IOutboundMessageTracker>(), Mock.Of<IChannelEventPublisher>());

        var response = await client.PostAsJsonAsync(
            "/internal/messages", new OutboundChannelMessage { To = "", Type = "text", Text = "hello" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        whatsAppClient.Verify(
            c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Send_WhatsAppApiFails_SurfacesErrorAndPublishesFailedStatus()
    {
        var whatsAppClient = new Mock<IWhatsAppCloudApiClient>();
        whatsAppClient
            .Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppSendResult(false, null, "131056", "rate limited"));
        var publisher = new Mock<IChannelEventPublisher>();

        var client = CreateClient(whatsAppClient.Object, Mock.Of<IOutboundMessageTracker>(), publisher.Object);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem-failed-1");

        var response = await client.PostAsJsonAsync(
            "/internal/messages", new OutboundChannelMessage { To = "5511999990000", Type = "text", Text = "hello" });

        Assert.False(response.IsSuccessStatusCode);
        publisher.Verify(
            p => p.PublishMessageStatusAsync(
                It.Is<MessageStatusEvent>(e => e.Status == MessageDeliveryStatus.Failed && e.Error!.Code == "131056"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Send_InteractivePayload_ReturnsAcceptedAndCallsSendInteractiveButtons()
    {
        var whatsAppClient = new Mock<IWhatsAppCloudApiClient>();
        whatsAppClient
            .Setup(c => c.SendInteractiveButtonsAsync(
                "5511999990000", "Escolha uma opção", It.IsAny<IReadOnlyList<OutboundButton>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppSendResult(true, "wamid.menu-1", null, null));
        var tracker = new Mock<IOutboundMessageTracker>();

        var client = CreateClient(whatsAppClient.Object, tracker.Object, Mock.Of<IChannelEventPublisher>());
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem-menu-1");

        var response = await client.PostAsJsonAsync(
            "/internal/messages",
            new OutboundChannelMessage
            {
                To = "5511999990000",
                Type = "interactive",
                Text = "Escolha uma opção",
                Buttons = [new OutboundButton("skill_a", "Opção A"), new OutboundButton("skill_b", "Opção B")]
            });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        tracker.Verify(t => t.MarkSent("wamid.menu-1"), Times.Once);
    }

    [Fact]
    public async Task Send_InteractivePayloadWithoutButtons_ReturnsBadRequestWithoutCallingWhatsAppApi()
    {
        var whatsAppClient = new Mock<IWhatsAppCloudApiClient>();
        var client = CreateClient(whatsAppClient.Object, Mock.Of<IOutboundMessageTracker>(), Mock.Of<IChannelEventPublisher>());
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem-menu-2");

        var response = await client.PostAsJsonAsync(
            "/internal/messages",
            new OutboundChannelMessage { To = "5511999990000", Type = "interactive", Text = "Escolha uma opção" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        whatsAppClient.Verify(
            c => c.SendInteractiveButtonsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<OutboundButton>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Send_InteractivePayloadWithTooManyButtons_ReturnsBadRequest()
    {
        var whatsAppClient = new Mock<IWhatsAppCloudApiClient>();
        var client = CreateClient(whatsAppClient.Object, Mock.Of<IOutboundMessageTracker>(), Mock.Of<IChannelEventPublisher>());
        client.DefaultRequestHeaders.Add("Idempotency-Key", "idem-menu-3");

        var response = await client.PostAsJsonAsync(
            "/internal/messages",
            new OutboundChannelMessage
            {
                To = "5511999990000",
                Type = "interactive",
                Text = "Escolha uma opção",
                Buttons =
                [
                    new OutboundButton("a", "A"),
                    new OutboundButton("b", "B"),
                    new OutboundButton("c", "C"),
                    new OutboundButton("d", "D")
                ]
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient CreateClient(
        IWhatsAppCloudApiClient whatsAppClient, IOutboundMessageTracker tracker, IChannelEventPublisher publisher)
    {
        var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            TestAuth.ConfigureSigningKey(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IWhatsAppCloudApiClient>();
                services.AddSingleton(whatsAppClient);
                services.RemoveAll<IOutboundMessageTracker>();
                services.AddSingleton(tracker);
                services.RemoveAll<IChannelEventPublisher>();
                services.AddSingleton(publisher);
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueToken());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TestAuth.TenantId);
        return client;
    }
}
