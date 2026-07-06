namespace whatsapp_bff.Adapters.Inbound.Messaging;

public interface IInboundWebhookQueue
{
    ValueTask EnqueueAsync(RawWebhookEnvelope envelope, CancellationToken cancellationToken);

    IAsyncEnumerable<RawWebhookEnvelope> ReadAllAsync(CancellationToken cancellationToken);
}
