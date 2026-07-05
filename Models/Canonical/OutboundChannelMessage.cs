namespace whatsapp_bff.Models.Canonical;

public class OutboundChannelMessage
{
    public required string To { get; init; }
    public required string Type { get; init; }
    public string? Text { get; init; }
}
