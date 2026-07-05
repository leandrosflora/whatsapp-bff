namespace whatsapp_bff.Models.Canonical;

public class InboundChannelMessage
{
    public required string MessageId { get; init; }
    public required string From { get; init; }
    public required string ConversationId { get; init; }
    public required ChannelMessageType Type { get; init; }
    public string? Text { get; init; }
    public InteractiveReply? Interactive { get; init; }
    public string? RawPayload { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
}
