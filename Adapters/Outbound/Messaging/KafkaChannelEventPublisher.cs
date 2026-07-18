using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Configuration;
using whatsapp_bff.Domain;
using whatsapp_bff.Platform;

namespace whatsapp_bff.Adapters.Outbound.Messaging;

public class KafkaChannelEventPublisher(
    IProducer<string, string> producer,
    IOptions<KafkaOptions> options,
    PlatformMetrics metrics,
    ILogger<KafkaChannelEventPublisher> logger) : IChannelEventPublisher
{
    public Task PublishMessageReceivedAsync(InboundChannelMessage message, CancellationToken cancellationToken) =>
        PublishAsync(options.Value.MessageReceivedTopic, message.ConversationId, message, message.MessageId, cancellationToken);

    public Task PublishMessageStatusAsync(MessageStatusEvent statusEvent, CancellationToken cancellationToken) =>
        PublishAsync(options.Value.MessageStatusTopic, statusEvent.ConversationId, statusEvent, statusEvent.MessageId, cancellationToken);

    public async Task PublishRawWebhookReceivedAsync(
        string correlationId,
        string partitionKey,
        string rawJson,
        CancellationToken cancellationToken)
    {
        var message = new Message<string, string>
        {
            Key = partitionKey,
            Value = rawJson,
            Headers = CreateTraceHeaders(correlationId)
        };

        await producer.ProduceAsync(options.Value.RawWebhookReceivedTopic, message, cancellationToken);
        metrics.Increment("channel_webhook_kafka_persisted_total");
    }

    private async Task PublishAsync<T>(
        string topic,
        string partitionKey,
        T value,
        string messageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(value);
            await producer.ProduceAsync(
                topic,
                new Message<string, string>
                {
                    Key = partitionKey,
                    Value = json,
                    Headers = CreateTraceHeaders(correlationId: null)
                },
                cancellationToken);
            metrics.Increment("channel_events_published_total", ("topic", topic));
        }
        catch (Exception ex)
        {
            metrics.Increment("channel_event_publish_failures_total", ("topic", topic));
            logger.LogError(ex, "Failed to publish event for message {MessageId} to Kafka topic {Topic}", messageId, topic);
        }
    }

    private static Headers CreateTraceHeaders(string? correlationId)
    {
        var headers = new Headers();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            headers.Add("CorrelationId", Encoding.UTF8.GetBytes(correlationId));
        }

        var activity = Activity.Current;
        if (activity is not null)
        {
            headers.Add("traceparent", Encoding.UTF8.GetBytes(activity.Id ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(activity.TraceStateString))
            {
                headers.Add("tracestate", Encoding.UTF8.GetBytes(activity.TraceStateString));
            }
        }
        return headers;
    }
}
