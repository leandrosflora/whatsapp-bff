using whatsapp_bff.Domain;

namespace whatsapp_bff.Application.Ports.Outbound;

public interface IChannelEventPublisher
{
    Task PublishMessageReceivedAsync(InboundChannelMessage message, CancellationToken cancellationToken);

    Task PublishMessageStatusAsync(MessageStatusEvent statusEvent, CancellationToken cancellationToken);
}
