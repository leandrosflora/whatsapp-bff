namespace whatsapp_bff.Processing;

public interface IMessageDedupeStore
{
    /// <summary>Returns true the first time a message ID is seen; false on any subsequent duplicate.</summary>
    bool TryMarkProcessed(string messageId);
}
