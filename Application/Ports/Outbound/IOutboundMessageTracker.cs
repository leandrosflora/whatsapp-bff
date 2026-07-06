namespace whatsapp_bff.Application.Ports.Outbound;

public interface IOutboundMessageTracker
{
    void MarkSent(string whatsAppMessageId);

    bool IsKnown(string whatsAppMessageId);
}
