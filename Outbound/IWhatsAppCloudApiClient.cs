namespace whatsapp_bff.Outbound;

public interface IWhatsAppCloudApiClient
{
    Task<WhatsAppSendResult> SendTextMessageAsync(string to, string text, CancellationToken cancellationToken);
}
