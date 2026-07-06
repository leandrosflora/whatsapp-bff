using whatsapp_bff.Adapters.Inbound.Http;

namespace whatsapp_bff.Adapters.Inbound.Messaging;

public record RawWebhookEnvelope(
    WhatsAppWebhookPayload Payload, string RawJson, DateTimeOffset ReceivedAt, string CorrelationId);
