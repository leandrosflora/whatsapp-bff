namespace whatsapp_bff.Application.Ports.Outbound;

public interface IWhatsAppCloudApiClient
{
    Task<WhatsAppSendResult> SendTextMessageAsync(string to, string text, CancellationToken cancellationToken);
}

public record WhatsAppSendResult(bool Success, string? WhatsAppMessageId, string? ErrorCode, string? ErrorMessage);
