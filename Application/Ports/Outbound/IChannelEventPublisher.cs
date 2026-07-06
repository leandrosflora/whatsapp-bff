using whatsapp_bff.Domain;

namespace whatsapp_bff.Application.Ports.Outbound;

public interface IChannelEventPublisher
{
    Task PublishMessageReceivedAsync(InboundChannelMessage message, CancellationToken cancellationToken);

    Task PublishMessageStatusAsync(MessageStatusEvent statusEvent, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the raw webhook delivery before it is acknowledged to the channel provider.
    /// Unlike the other publish methods, failures are not swallowed: the caller relies on this
    /// throwing so it can reject the HTTP delivery instead of acking a message that was never
    /// durably recorded.
    /// </summary>
    Task PublishRawWebhookReceivedAsync(
        string correlationId, string partitionKey, string rawJson, CancellationToken cancellationToken);
}
