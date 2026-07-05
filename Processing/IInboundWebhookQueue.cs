namespace whatsapp_bff.Processing;

public interface IInboundWebhookQueue
{
    ValueTask EnqueueAsync(RawWebhookEnvelope envelope, CancellationToken cancellationToken);

    IAsyncEnumerable<RawWebhookEnvelope> ReadAllAsync(CancellationToken cancellationToken);
}
