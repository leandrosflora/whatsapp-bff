using System.Threading.Channels;

namespace whatsapp_bff.Adapters.Inbound.Messaging;

public class InboundWebhookQueue : IInboundWebhookQueue
{
    private readonly Channel<RawWebhookEnvelope> _channel = Channel.CreateUnbounded<RawWebhookEnvelope>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ValueTask EnqueueAsync(RawWebhookEnvelope envelope, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(envelope, cancellationToken);

    public IAsyncEnumerable<RawWebhookEnvelope> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
