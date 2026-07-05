using whatsapp_bff.Models.Canonical;

namespace whatsapp_bff.Orchestrator;

public interface IOrchestratorClient
{
    /// <summary>Returns true if the Orchestrator accepted the message (2xx); false on any failure, including exhausted retries.</summary>
    Task<bool> ForwardMessageAsync(InboundChannelMessage message, CancellationToken cancellationToken);
}
