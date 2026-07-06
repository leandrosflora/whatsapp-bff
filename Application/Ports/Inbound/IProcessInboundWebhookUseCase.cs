using whatsapp_bff.Domain;

namespace whatsapp_bff.Application.Ports.Inbound;

public interface IProcessInboundWebhookUseCase
{
    Task ExecuteAsync(
        IReadOnlyList<InboundChannelMessage> messages,
        IReadOnlyList<MessageStatusEvent> statusEvents,
        CancellationToken cancellationToken);
}
