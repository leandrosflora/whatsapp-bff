using whatsapp_bff.Application.Ports.Inbound;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Domain;

namespace whatsapp_bff.Application.UseCases;

public class SendOutboundMessageUseCase(
    IWhatsAppCloudApiClient whatsAppClient,
    IOutboundMessageTracker tracker,
    IChannelEventPublisher eventPublisher,
    ILogger<SendOutboundMessageUseCase> logger) : ISendOutboundMessageUseCase
{
    public async Task<SendOutboundMessageResult> ExecuteAsync(OutboundChannelMessage request, CancellationToken cancellationToken)
    {
        var result = await whatsAppClient.SendTextMessageAsync(request.To, request.Text!, cancellationToken);

        if (result is { Success: true, WhatsAppMessageId: not null })
        {
            tracker.MarkSent(result.WhatsAppMessageId);
            return new SendOutboundMessageResult(true, result.WhatsAppMessageId, null);
        }

        logger.LogWarning("Outbound send failed for recipient {To}: {Error}", request.To, result.ErrorMessage);

        await eventPublisher.PublishMessageStatusAsync(
            new MessageStatusEvent
            {
                MessageId = result.WhatsAppMessageId ?? Guid.NewGuid().ToString(),
                ConversationId = request.To,
                Status = MessageDeliveryStatus.Failed,
                Timestamp = DateTimeOffset.UtcNow,
                Error = new StatusError(result.ErrorCode ?? "unknown", result.ErrorMessage ?? "Failed to send message"),
                IsKnownMessage = false
            },
            cancellationToken);

        return new SendOutboundMessageResult(false, null, result.ErrorMessage);
    }
}
