using Microsoft.Extensions.Caching.Memory;

namespace whatsapp_bff.Outbound;

public class InMemoryOutboundMessageTracker(IMemoryCache cache) : IOutboundMessageTracker
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public void MarkSent(string whatsAppMessageId) => cache.Set(CacheKey(whatsAppMessageId), true, Ttl);

    public bool IsKnown(string whatsAppMessageId) => cache.TryGetValue(CacheKey(whatsAppMessageId), out _);

    private static string CacheKey(string whatsAppMessageId) => $"outbound-sent:{whatsAppMessageId}";
}
