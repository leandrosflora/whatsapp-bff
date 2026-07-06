using whatsapp_bff.Domain;

namespace whatsapp_bff.Application.Ports.Outbound;

public interface IOrchestratorClient
{
    /// <summary>Returns true if the Orchestrator accepted the message (2xx); false on any failure, including exhausted retries.</summary>
    Task<bool> ForwardMessageAsync(InboundChannelMessage message, CancellationToken cancellationToken);
}
