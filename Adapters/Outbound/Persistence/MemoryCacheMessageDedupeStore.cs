using Microsoft.Extensions.Caching.Memory;
using whatsapp_bff.Application.Ports.Outbound;

namespace whatsapp_bff.Adapters.Outbound.Persistence;

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
