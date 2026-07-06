using whatsapp_bff.Adapters.Inbound.Http.Mapping;
using whatsapp_bff.Application.Ports.Inbound;

namespace whatsapp_bff.Adapters.Inbound.Messaging;

public class InboundWebhookProcessingService(
    IInboundWebhookQueue queue,
    IWhatsAppPayloadMapper mapper,
    IProcessInboundWebhookUseCase useCase,
    ILogger<InboundWebhookProcessingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var envelope in queue.ReadAllAsync(stoppingToken))
        {
            using var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = envelope.CorrelationId
            });

            try
            {
                var messages = mapper.MapInboundMessages(envelope.Payload, envelope.RawJson);
                var statusEvents = mapper.MapStatusEvents(envelope.Payload);
                await useCase.ExecuteAsync(messages, statusEvents, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error processing webhook envelope received at {ReceivedAt}", envelope.ReceivedAt);
            }
        }
    }
}
