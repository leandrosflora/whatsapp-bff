using whatsapp_bff.Adapters.Inbound.Http;
using whatsapp_bff.Domain;

namespace whatsapp_bff.Adapters.Inbound.Http.Mapping;

public interface IWhatsAppPayloadMapper
{
    IReadOnlyList<InboundChannelMessage> MapInboundMessages(WhatsAppWebhookPayload payload, string rawPayload);

    IReadOnlyList<MessageStatusEvent> MapStatusEvents(WhatsAppWebhookPayload payload);
}
