using whatsapp_bff.Events;
using whatsapp_bff.Mapping;
using whatsapp_bff.Orchestrator;
using whatsapp_bff.Outbound;

namespace whatsapp_bff.Processing;

public class InboundWebhookProcessingService(
    IInboundWebhookQueue queue,
    IWhatsAppPayloadMapper mapper,
    IOrchestratorClient orchestratorClient,
    IChannelEventPublisher eventPublisher,
    IOutboundMessageTracker outboundMessageTracker,
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
                await ProcessEnvelopeAsync(envelope, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error processing webhook envelope received at {ReceivedAt}", envelope.ReceivedAt);
            }
        }
    }

    private async Task ProcessEnvelopeAsync(RawWebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        var inboundMessages = mapper.MapInboundMessages(envelope.Payload, envelope.RawJson);
        foreach (var message in inboundMessages)
        {
            var forwarded = await orchestratorClient.ForwardMessageAsync(message, cancellationToken);
            if (forwarded)
            {
                await eventPublisher.PublishMessageReceivedAsync(message, cancellationToken);
                logger.LogInformation(
                    "Forwarded inbound message {MessageId} from {ConversationId} to Orchestrator and published message.received",
                    message.MessageId,
                    message.ConversationId);
            }
            else
            {
                logger.LogWarning(
                    "Inbound message {MessageId} from {ConversationId} was not forwarded to the Orchestrator",
                    message.MessageId,
                    message.ConversationId);
            }
        }

        var statusEvents = mapper.MapStatusEvents(envelope.Payload);
        foreach (var statusEvent in statusEvents)
        {
            var reconciled = new Models.Canonical.MessageStatusEvent
            {
                MessageId = statusEvent.MessageId,
                ConversationId = statusEvent.ConversationId,
                Status = statusEvent.Status,
                Timestamp = statusEvent.Timestamp,
                Error = statusEvent.Error,
                IsKnownMessage = outboundMessageTracker.IsKnown(statusEvent.MessageId)
            };

            await eventPublisher.PublishMessageStatusAsync(reconciled, cancellationToken);
            logger.LogInformation(
                "Published message.status {Status} for {MessageId} (known outbound message: {IsKnownMessage})",
                reconciled.Status,
                reconciled.MessageId,
                reconciled.IsKnownMessage);
        }
    }
}
