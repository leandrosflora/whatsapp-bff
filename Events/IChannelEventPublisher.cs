using whatsapp_bff.Models.Canonical;

namespace whatsapp_bff.Events;

public interface IChannelEventPublisher
{
    Task PublishMessageReceivedAsync(InboundChannelMessage message, CancellationToken cancellationToken);

    Task PublishMessageStatusAsync(MessageStatusEvent statusEvent, CancellationToken cancellationToken);
}
