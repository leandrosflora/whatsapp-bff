namespace whatsapp_bff.Application.Ports.Outbound;

public enum OutboundDeliveryAcquireStatus
{
    Acquired,
    InProgress,
    Completed
}

public sealed record OutboundDeliveryLease(
    OutboundDeliveryAcquireStatus Status,
    string? MessageId = null);

public interface IOutboundDeliveryStore
{
    Task<OutboundDeliveryLease> TryAcquireAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        string tenantId,
        string idempotencyKey,
        string messageId,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
