using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using whatsapp_bff.Adapters.Inbound.Http.Mapping;
using whatsapp_bff.Adapters.Inbound.Messaging;
using whatsapp_bff.Application.Ports.Inbound;
using whatsapp_bff.Configuration;
using whatsapp_bff.Domain;
using whatsapp_bff.Platform;
using Xunit;

namespace whatsapp_bff.Tests.Adapters.Inbound.Messaging;

public class KafkaWebhookConsumerServiceTests
{
    private static readonly KafkaOptions Options = new()
    {
        BootstrapServers = "localhost:9092",
        RawWebhookReceivedTopic = "channel.webhook.received",
        WebhookConsumerGroupId = "test-group"
    };

    [Fact]
    public async Task ProcessMessageAsync_UseCaseForwardsSuccessfully_ReturnsTrue()
    {
        var useCase = new Mock<IProcessInboundWebhookUseCase>();
        useCase
            .Setup(u => u.ExecuteAsync(
                It.IsAny<IReadOnlyList<InboundChannelMessage>>(),
                It.IsAny<IReadOnlyList<MessageStatusEvent>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = CreateService(useCase.Object);

        var result = BuildConsumeResult(TextMessagePayload("wamid.1"), "corr-1");

        var handled = await service.ProcessMessageAsync(result, CancellationToken.None);

        Assert.True(handled);
        useCase.Verify(
            u => u.ExecuteAsync(
                It.Is<IReadOnlyList<InboundChannelMessage>>(m => m.Count == 1 && m[0].MessageId == "wamid.1"),
                It.IsAny<IReadOnlyList<MessageStatusEvent>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMessageAsync_UseCaseFailsToForward_ReturnsFalse()
    {
        var useCase = new Mock<IProcessInboundWebhookUseCase>();
        useCase
            .Setup(u => u.ExecuteAsync(
                It.IsAny<IReadOnlyList<InboundChannelMessage>>(),
                It.IsAny<IReadOnlyList<MessageStatusEvent>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = CreateService(useCase.Object);

        var result = BuildConsumeResult(TextMessagePayload("wamid.2"), "corr-2");

        var handled = await service.ProcessMessageAsync(result, CancellationToken.None);

        Assert.False(handled);
    }

    [Fact]
    public async Task ProcessMessageAsync_UnparseableJson_ReturnsFalseAndDropsWithoutCallingUseCase()
    {
        // Poison messages (invalid JSON, null payload) are routed to the DLQ by the caller
        // (RunLoop), not retried against the same offset - ProcessMessageAsync signals that
        // by returning false here (see ProcessingResult.PoisonMessage), distinct from a
        // transient failure that should be retried. See docs/runbook.md §7 (Kafka, retry e DLQ).
        var useCase = new Mock<IProcessInboundWebhookUseCase>();
        var service = CreateService(useCase.Object);

        var result = BuildConsumeResult("not-valid-json", "corr-3");

        var handled = await service.ProcessMessageAsync(result, CancellationToken.None);

        Assert.False(handled);
        useCase.Verify(
            u => u.ExecuteAsync(
                It.IsAny<IReadOnlyList<InboundChannelMessage>>(),
                It.IsAny<IReadOnlyList<MessageStatusEvent>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static KafkaWebhookConsumerService CreateService(IProcessInboundWebhookUseCase useCase) =>
        new(
            Mock.Of<IConsumer<string, string>>(),
            Mock.Of<IProducer<string, string>>(),
            Microsoft.Extensions.Options.Options.Create(Options),
            new WhatsAppPayloadMapper(),
            useCase,
            new PlatformMetrics(),
            NullLogger<KafkaWebhookConsumerService>.Instance);

    private static ConsumeResult<string, string> BuildConsumeResult(string rawJson, string correlationId)
    {
        var headers = new Headers { { "CorrelationId", Encoding.UTF8.GetBytes(correlationId) } };
        return new ConsumeResult<string, string>
        {
            Topic = Options.RawWebhookReceivedTopic,
            Partition = new Partition(0),
            Offset = new Offset(1),
            Message = new Message<string, string>
            {
                Key = "5511999990000",
                Value = rawJson,
                Headers = headers
            }
        };
    }

    private static string TextMessagePayload(string messageId) => $$"""
    {
      "entry": [{
        "changes": [{
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
