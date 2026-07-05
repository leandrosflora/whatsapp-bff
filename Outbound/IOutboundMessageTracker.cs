namespace whatsapp_bff.Outbound;

public interface IOutboundMessageTracker
{
    void MarkSent(string whatsAppMessageId);

    bool IsKnown(string whatsAppMessageId);
}
