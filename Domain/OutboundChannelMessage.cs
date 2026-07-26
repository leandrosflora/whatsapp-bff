namespace whatsapp_bff.Domain;

public class OutboundChannelMessage
{
    public required string To { get; init; }
    public required string Type { get; init; }
    public string? Text { get; init; }

    /// <summary>Required when Type is "interactive" - the flow-selection menu's buttons (up to
    /// 3, WhatsApp's own limit for a button-type interactive message).</summary>
    public IReadOnlyList<OutboundButton>? Buttons { get; init; }
}

public record OutboundButton(string Id, string Title);
