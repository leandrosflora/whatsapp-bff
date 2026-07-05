using whatsapp_bff.Models.WhatsApp;

namespace whatsapp_bff.Processing;

public record RawWebhookEnvelope(
    WhatsAppWebhookPayload Payload, string RawJson, DateTimeOffset ReceivedAt, string CorrelationId);
