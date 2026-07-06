using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Configuration;
using whatsapp_bff.Domain;

namespace whatsapp_bff.Adapters.Outbound.Messaging;

public class KafkaChannelEventPublisher(
    IProducer<string, string> producer,
    IOptions<KafkaOptions> options,
    ILogger<KafkaChannelEventPublisher> logger) : IChannelEventPublisher
{
    public Task PublishMessageReceivedAsync(InboundChannelMessage message, CancellationToken cancellationToken) =>
        PublishAsync(options.Value.MessageReceivedTopic, message.ConversationId, message, message.MessageId, cancellationToken);

    public Task PublishMessageStatusAsync(MessageStatusEvent statusEvent, CancellationToken cancellationToken) =>
        PublishAsync(options.Value.MessageStatusTopic, statusEvent.ConversationId, statusEvent, statusEvent.MessageId, cancellationToken);

    public async Task PublishRawWebhookReceivedAsync(
        string correlationId, string partitionKey, string rawJson, CancellationToken cancellationToken)
    {
        // Deliberately not wrapped in try/catch: the webhook endpoint must know if this failed
        // so it can reject the delivery (503) instead of acking a message that was never persisted.
        var message = new Message<string, string>
        {
            Key = partitionKey,
            Value = rawJson,
            Headers = new Headers { { "CorrelationId", System.Text.Encoding.UTF8.GetBytes(correlationId) } }
        };

        await producer.ProduceAsync(options.Value.RawWebhookReceivedTopic, message, cancellationToken);
    }

    private async Task PublishAsync<T>(
        string topic, string partitionKey, T value, string messageId, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(value);
            await producer.ProduceAsync(
                topic,
                new Message<string, string> { Key = partitionKey, Value = json },
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish event for message {MessageId} to Kafka topic {Topic}", messageId, topic);
        }
    }
}
