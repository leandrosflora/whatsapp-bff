using whatsapp_bff.Domain;

namespace whatsapp_bff.Application.Ports.Inbound;

public interface IProcessInboundWebhookUseCase
{
    /// <summary>
    /// Returns <c>true</c> only if every message was successfully forwarded to the Orchestrator
    /// (vacuously <c>true</c> if <paramref name="messages"/> is empty). Callers that need
    /// at-least-once delivery to the Orchestrator (e.g. a Kafka consumer deciding whether to
    /// commit its offset) rely on this signal; status event publishing never affects it.
    /// </summary>
    Task<bool> ExecuteAsync(
        IReadOnlyList<InboundChannelMessage> messages,
        IReadOnlyList<MessageStatusEvent> statusEvents,
        CancellationToken cancellationToken);
}
