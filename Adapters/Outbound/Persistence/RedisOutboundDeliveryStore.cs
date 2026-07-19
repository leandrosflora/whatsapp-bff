using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Configuration;

namespace whatsapp_bff.Adapters.Outbound.Persistence;

public sealed class RedisOutboundDeliveryStore(
    IConnectionMultiplexer connection,
    IOptions<RedisOptions> options) : IOutboundDeliveryStore
{
    private const string PendingValue = "pending";

    public async Task<OutboundDeliveryLease> TryAcquireAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = connection.GetDatabase();
        var key = BuildKey(tenantId, idempotencyKey);
        var acquired = await database.StringSetAsync(
            key,
            PendingValue,
            TimeSpan.FromSeconds(Math.Max(30, options.Value.PendingTtlSeconds)),
            When.NotExists);
        cancellationToken.ThrowIfCancellationRequested();
        if (acquired)
        {
            return new OutboundDeliveryLease(OutboundDeliveryAcquireStatus.Acquired);
        }

        var existing = await database.StringGetAsync(key);
        cancellationToken.ThrowIfCancellationRequested();
        if (existing.HasValue && existing.ToString().StartsWith("completed:", StringComparison.Ordinal))
        {
            return new OutboundDeliveryLease(
                OutboundDeliveryAcquireStatus.Completed,
                existing.ToString()["completed:".Length..]);
        }
        return new OutboundDeliveryLease(OutboundDeliveryAcquireStatus.InProgress);
    }

    public async Task CompleteAsync(
        string tenantId,
        string idempotencyKey,
        string messageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await connection.GetDatabase().StringSetAsync(
            BuildKey(tenantId, idempotencyKey),
            $"completed:{messageId}",
            TimeSpan.FromSeconds(Math.Max(300, options.Value.CompletedTtlSeconds)));
    }

    public async Task ReleaseAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string script = """
            if redis.call('GET', KEYS[1]) == ARGV[1] then
                return redis.call('DEL', KEYS[1])
            end
            return 0
            """;
        await connection.GetDatabase().ScriptEvaluateAsync(
            script,
            [BuildKey(tenantId, idempotencyKey)],
            [PendingValue]);
    }

    private static RedisKey BuildKey(string tenantId, string idempotencyKey)
    {
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey))).ToLowerInvariant();
        return $"outbound:{tenantId}:{digest}";
    }
}
