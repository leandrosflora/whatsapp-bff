namespace whatsapp_bff.Application.Ports.Outbound;

public enum MessageDedupeReservationStatus
{
    Acquired,
    InProgress,
    Completed
}

public interface IMessageDedupeStore
{
    /// <summary>
    /// Reserves a message ID before its webhook payload is persisted. A concurrent delivery receives
    /// <see cref="MessageDedupeReservationStatus.InProgress"/> and must be retried instead of acknowledged.
    /// </summary>
    MessageDedupeReservationStatus TryReserve(string messageId);

    /// <summary>Marks a reservation as completed only after Kafka confirms the durable write.</summary>
    void MarkCompleted(string messageId);

    /// <summary>Releases a pending reservation when the durable write fails, allowing provider redelivery.</summary>
    void Release(string messageId);
}
