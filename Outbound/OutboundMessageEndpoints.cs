using whatsapp_bff.Events;
using whatsapp_bff.Models.Canonical;

namespace whatsapp_bff.Outbound;

public static class OutboundMessageEndpoints
{
    public static IEndpointRouteBuilder MapOutboundMessageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/internal/messages", HandleSendAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleSendAsync(
        OutboundChannelMessage request,
        IWhatsAppCloudApiClient whatsAppClient,
        IOutboundMessageTracker tracker,
        IChannelEventPublisher eventPublisher,
        ILogger<OutboundMessageLogCategory> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.To) || string.IsNullOrWhiteSpace(request.Text))
        {
            return Results.BadRequest(new { error = "'to' and 'text' are required." });
        }

        var result = await whatsAppClient.SendTextMessageAsync(request.To, request.Text, cancellationToken);

        if (result is { Success: true, WhatsAppMessageId: not null })
        {
            tracker.MarkSent(result.WhatsAppMessageId);
            return Results.Accepted(value: new { messageId = result.WhatsAppMessageId });
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

        return Results.Problem(detail: result.ErrorMessage, statusCode: StatusCodes.Status502BadGateway);
    }
}

public sealed class OutboundMessageLogCategory;
