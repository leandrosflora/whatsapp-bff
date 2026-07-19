using System.Collections.Concurrent;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Domain;

namespace whatsapp_bff.Tests.Adapters.Outbound.Messaging;

/// <summary>
/// Records published events for assertions instead of talking to a real Kafka broker.
/// </summary>
public class FakeChannelEventPublisher : IChannelEventPublisher
{
    public ConcurrentQueue<InboundChannelMessage> MessageReceivedEvents { get; } = new();

    public ConcurrentQueue<MessageStatusEvent> MessageStatusEvents { get; } = new();

    public ConcurrentQueue<(string CorrelationId, string PartitionKey, string RawJson)> RawWebhookEvents { get; } = new();

    public bool ThrowOnRawWebhookPublish { get; set; }

    public Task PublishMessageReceivedAsync(InboundChannelMessage message, CancellationToken cancellationToken)
    {
        MessageReceivedEvents.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task PublishMessageStatusAsync(MessageStatusEvent statusEvent, CancellationToken cancellationToken)
    {
        MessageStatusEvents.Enqueue(statusEvent);
        return Task.CompletedTask;
    }

    public Task PublishRawWebhookReceivedAsync(
        string correlationId, string partitionKey, string rawJson, CancellationToken cancellationToken)
    {
        if (ThrowOnRawWebhookPublish)
        {
            throw new InvalidOperationException("Simulated Kafka broker unavailability");
        }

        RawWebhookEvents.Enqueue((correlationId, partitionKey, rawJson));
        return Task.CompletedTask;
    }
}
