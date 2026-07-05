namespace whatsapp_bff.Models.Canonical;

public class MessageStatusEvent
{
    public required string MessageId { get; init; }
    public required string ConversationId { get; init; }
    public required MessageDeliveryStatus Status { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public StatusError? Error { get; init; }
    public bool IsKnownMessage { get; init; } = true;
}
