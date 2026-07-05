using System.Text.Json;
using whatsapp_bff.Models.Canonical;
using whatsapp_bff.Models.WhatsApp;

namespace whatsapp_bff.Mapping;

public class WhatsAppPayloadMapper : IWhatsAppPayloadMapper
{
    public IReadOnlyList<InboundChannelMessage> MapInboundMessages(WhatsAppWebhookPayload payload, string rawPayload)
    {
        var results = new List<InboundChannelMessage>();

        foreach (var message in EnumerateMessages(payload))
        {
            if (message.Id is null || message.From is null)
            {
                continue;
            }

            var receivedAt = ParseUnixTimestamp(message.Timestamp);

            results.Add(message.Type switch
            {
                "text" => new InboundChannelMessage
                {
                    MessageId = message.Id,
                    From = message.From,
                    ConversationId = message.From,
                    Type = ChannelMessageType.Text,
                    Text = message.Text?.Body,
                    ReceivedAt = receivedAt
                },
                "interactive" => new InboundChannelMessage
                {
                    MessageId = message.Id,
                    From = message.From,
                    ConversationId = message.From,
                    Type = ChannelMessageType.Interactive,
                    Interactive = MapInteractiveReply(message.Interactive),
                    ReceivedAt = receivedAt
                },
                _ => new InboundChannelMessage
                {
                    MessageId = message.Id,
                    From = message.From,
                    ConversationId = message.From,
                    Type = ChannelMessageType.Unsupported,
                    RawPayload = JsonSerializer.Serialize(message),
                    ReceivedAt = receivedAt
                }
            });
        }

        return results;
    }

    public IReadOnlyList<MessageStatusEvent> MapStatusEvents(WhatsAppWebhookPayload payload)
    {
        var results = new List<MessageStatusEvent>();

        foreach (var status in EnumerateStatuses(payload))
        {
            if (status.Id is null || status.RecipientId is null || !TryMapStatus(status.Status, out var deliveryStatus))
            {
                continue;
            }

            var error = status.Errors is { Count: > 0 }
                ? new StatusError(
                    status.Errors[0].Code.ToString(),
                    status.Errors[0].Message ?? status.Errors[0].Title ?? "Unknown error")
                : null;

            results.Add(new MessageStatusEvent
            {
                MessageId = status.Id,
                ConversationId = status.RecipientId,
                Status = deliveryStatus,
                Timestamp = ParseUnixTimestamp(status.Timestamp),
                Error = error
            });
        }

        return results;
    }

    private static IEnumerable<WhatsAppMessage> EnumerateMessages(WhatsAppWebhookPayload payload) =>
        payload.Entry
            .SelectMany(entry => entry.Changes)
            .Select(change => change.Value)
            .Where(value => value?.Messages is not null)
            .SelectMany(value => value!.Messages!);

    private static IEnumerable<WhatsAppStatus> EnumerateStatuses(WhatsAppWebhookPayload payload) =>
        payload.Entry
            .SelectMany(entry => entry.Changes)
            .Select(change => change.Value)
            .Where(value => value?.Statuses is not null)
            .SelectMany(value => value!.Statuses!);

    private static InteractiveReply? MapInteractiveReply(WhatsAppInteractive? interactive)
    {
        var option = interactive?.ButtonReply ?? interactive?.ListReply;
        if (option?.Id is null || option.Title is null)
        {
            return null;
        }

        return new InteractiveReply(option.Id, option.Title);
    }

    private static bool TryMapStatus(string? status, out MessageDeliveryStatus deliveryStatus)
    {
        switch (status?.ToLowerInvariant())
        {
            case "sent":
                deliveryStatus = MessageDeliveryStatus.Sent;
                return true;
            case "delivered":
                deliveryStatus = MessageDeliveryStatus.Delivered;
                return true;
            case "read":
                deliveryStatus = MessageDeliveryStatus.Read;
                return true;
            case "failed":
                deliveryStatus = MessageDeliveryStatus.Failed;
                return true;
            default:
                deliveryStatus = default;
                return false;
        }
    }

    private static DateTimeOffset ParseUnixTimestamp(string? timestamp) =>
        long.TryParse(timestamp, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UtcNow;
}
