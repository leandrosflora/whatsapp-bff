using whatsapp_bff.Domain;

namespace whatsapp_bff.Application.Ports.Outbound;

public interface IWhatsAppCloudApiClient
{
    Task<WhatsAppSendResult> SendTextMessageAsync(string to, string text, CancellationToken cancellationToken);

    /// <summary>Sends a WhatsApp interactive button message (e.g. the flow-selection menu) -
    /// at most 3 buttons, WhatsApp's own limit for this message type.</summary>
    Task<WhatsAppSendResult> SendInteractiveButtonsAsync(
        string to, string bodyText, IReadOnlyList<OutboundButton> buttons, CancellationToken cancellationToken);

    /// <summary>Marks the inbound message as read and shows the "typing..." indicator for up to 25s. Best-effort: never throws.</summary>
    Task SendTypingIndicatorAsync(string incomingMessageId, CancellationToken cancellationToken);
}

public record WhatsAppSendResult(bool Success, string? WhatsAppMessageId, string? ErrorCode, string? ErrorMessage);
