using Microsoft.Extensions.Caching.Memory;

namespace whatsapp_bff.Processing;

public class MemoryCacheMessageDedupeStore(IMemoryCache cache) : IMessageDedupeStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public bool TryMarkProcessed(string messageId)
    {
        if (cache.TryGetValue(messageId, out _))
        {
            return false;
        }

        cache.Set(messageId, true, Ttl);
        return true;
    }
}
