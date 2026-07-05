using whatsapp_bff.Models.Canonical;
using whatsapp_bff.Models.WhatsApp;

namespace whatsapp_bff.Mapping;

public interface IWhatsAppPayloadMapper
{
    IReadOnlyList<InboundChannelMessage> MapInboundMessages(WhatsAppWebhookPayload payload, string rawPayload);

    IReadOnlyList<MessageStatusEvent> MapStatusEvents(WhatsAppWebhookPayload payload);
}
