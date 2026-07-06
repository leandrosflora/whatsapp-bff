using whatsapp_bff.Application.Ports.Inbound;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Domain;

namespace whatsapp_bff.Application.UseCases;

public class ProcessInboundWebhookUseCase(
    IOrchestratorClient orchestratorClient,
    IChannelEventPublisher eventPublisher,
    IOutboundMessageTracker outboundMessageTracker,
    ILogger<ProcessInboundWebhookUseCase> logger) : IProcessInboundWebhookUseCase
{
    public async Task ExecuteAsync(
        IReadOnlyList<InboundChannelMessage> messages,
        IReadOnlyList<MessageStatusEvent> statusEvents,
        CancellationToken cancellationToken)
    {
        foreach (var message in messages)
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

        foreach (var statusEvent in statusEvents)
        {
            var reconciled = new MessageStatusEvent
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
