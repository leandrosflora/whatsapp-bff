using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using whatsapp_bff.Configuration;
using whatsapp_bff.Adapters.Outbound.Messaging;
using whatsapp_bff.Domain;
using whatsapp_bff.Platform;
using Xunit;

namespace whatsapp_bff.Tests.Adapters.Outbound.Messaging;

public class KafkaChannelEventPublisherTests
{
    private static readonly KafkaOptions Options = new()
    {
        BootstrapServers = "localhost:9092",
        MessageReceivedTopic = "channel.message.received",
        MessageStatusTopic = "channel.message.status",
        RawWebhookReceivedTopic = "channel.webhook.received"
    };

    [Fact]
    public async Task PublishMessageReceivedAsync_ProducerSucceeds_PublishesToReceivedTopicWithConversationKey()
    {
        var producer = new Mock<IProducer<string, string>>();
        producer
            .Setup(p => p.ProduceAsync("channel.message.received", It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult<string, string>());

        var publisher = new KafkaChannelEventPublisher(
            producer.Object,
            Microsoft.Extensions.Options.Options.Create(Options),
            new PlatformMetrics(),
            NullLogger<KafkaChannelEventPublisher>.Instance);

        var message = new InboundChannelMessage
        {
            MessageId = "wamid.1",
            From = "5511999990000",
            ConversationId = "5511999990000",
            Type = ChannelMessageType.Text,
            Text = "hi",
            ReceivedAt = DateTimeOffset.UtcNow
        };

        await publisher.PublishMessageReceivedAsync(message, CancellationToken.None);

        producer.Verify(
            p => p.ProduceAsync(
                "channel.message.received",
                It.Is<Message<string, string>>(m => m.Key == "5511999990000" && m.Value.Contains("wamid.1")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishMessageStatusAsync_BrokerUnavailable_DoesNotThrow()
    {
        var producer = new Mock<IProducer<string, string>>();
        producer
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProduceException<string, string>(
                new Error(ErrorCode.Local_Transport, "broker unavailable"),
                new DeliveryResult<string, string>()));

        var publisher = new KafkaChannelEventPublisher(
            producer.Object,
            Microsoft.Extensions.Options.Options.Create(Options),
            new PlatformMetrics(),
            NullLogger<KafkaChannelEventPublisher>.Instance);

        var statusEvent = new MessageStatusEvent
        {
            MessageId = "wamid.out.1",
            ConversationId = "5511999990000",
            Status = MessageDeliveryStatus.Delivered,
            Timestamp = DateTimeOffset.UtcNow
        };

        var exception = await Record.ExceptionAsync(() =>
            publisher.PublishMessageStatusAsync(statusEvent, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task PublishRawWebhookReceivedAsync_ProducerSucceeds_PublishesToRawTopicWithCorrelationHeader()
    {
        var producer = new Mock<IProducer<string, string>>();
        producer
            .Setup(p => p.ProduceAsync("channel.webhook.received", It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult<string, string>());

        var publisher = new KafkaChannelEventPublisher(
            producer.Object,
            Microsoft.Extensions.Options.Options.Create(Options),
            new PlatformMetrics(),
            NullLogger<KafkaChannelEventPublisher>.Instance);

        await publisher.PublishRawWebhookReceivedAsync(
            "corr-1", "5511999990000", "{\"raw\":true}", CancellationToken.None);

        producer.Verify(
            p => p.ProduceAsync(
                "channel.webhook.received",
                It.Is<Message<string, string>>(m =>
                    m.Key == "5511999990000" &&
                    m.Value == "{\"raw\":true}" &&
                    m.Headers.Any(h => h.Key == "CorrelationId")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishRawWebhookReceivedAsync_BrokerUnavailable_PropagatesException()
    {
        var producer = new Mock<IProducer<string, string>>();
        producer
            .Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProduceException<string, string>(
                new Error(ErrorCode.Local_Transport, "broker unavailable"),
                new DeliveryResult<string, string>()));

        var publisher = new KafkaChannelEventPublisher(
            producer.Object,
            Microsoft.Extensions.Options.Options.Create(Options),
            new PlatformMetrics(),
            NullLogger<KafkaChannelEventPublisher>.Instance);

        await Assert.ThrowsAsync<ProduceException<string, string>>(() =>
            publisher.PublishRawWebhookReceivedAsync("corr-1", "5511999990000", "{}", CancellationToken.None));
    }
}
