using Microsoft.Extensions.Caching.Memory;
using whatsapp_bff.Application.Ports.Outbound;

namespace whatsapp_bff.Adapters.Outbound.Persistence;

public class MemoryCacheMessageDedupeStore(IMemoryCache cache) : IMessageDedupeStore
{
    private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CompletedTtl = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();

    public MessageDedupeReservationStatus TryReserve(string messageId)
    {
        lock (_gate)
        {
            if (cache.TryGetValue<DedupeEntry>(messageId, out var existing) && existing is not null)
            {
                return existing.State == DedupeState.Completed
                    ? MessageDedupeReservationStatus.Completed
                    : MessageDedupeReservationStatus.InProgress;
            }

            cache.Set(messageId, new DedupeEntry(DedupeState.Pending), PendingTtl);
            return MessageDedupeReservationStatus.Acquired;
        }
    }

    public void MarkCompleted(string messageId)
    {
        lock (_gate)
        {
            cache.Set(messageId, new DedupeEntry(DedupeState.Completed), CompletedTtl);
        }
    }

    public void Release(string messageId)
    {
        lock (_gate)
        {
            if (cache.TryGetValue<DedupeEntry>(messageId, out var existing)
                && existing?.State == DedupeState.Pending)
            {
                cache.Remove(messageId);
            }
        }
    }

    private enum DedupeState
    {
        Pending,
        Completed
    }

    private sealed record DedupeEntry(DedupeState State);
}
