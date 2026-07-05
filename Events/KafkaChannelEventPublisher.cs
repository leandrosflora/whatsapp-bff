using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using whatsapp_bff.Configuration;
using whatsapp_bff.Models.Canonical;

namespace whatsapp_bff.Events;

public class KafkaChannelEventPublisher(
    IProducer<string, string> producer,
    IOptions<KafkaOptions> options,
    ILogger<KafkaChannelEventPublisher> logger) : IChannelEventPublisher
{
    public Task PublishMessageReceivedAsync(InboundChannelMessage message, CancellationToken cancellationToken) =>
        PublishAsync(options.Value.MessageReceivedTopic, message.ConversationId, message, message.MessageId, cancellationToken);

    public Task PublishMessageStatusAsync(MessageStatusEvent statusEvent, CancellationToken cancellationToken) =>
        PublishAsync(options.Value.MessageStatusTopic, statusEvent.ConversationId, statusEvent, statusEvent.MessageId, cancellationToken);

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
