using System.Net.Http.Json;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Domain;

namespace whatsapp_bff.Adapters.Outbound.Http;

public class OrchestratorClient(HttpClient httpClient, ILogger<OrchestratorClient> logger) : IOrchestratorClient
{
    public async Task<bool> ForwardMessageAsync(InboundChannelMessage message, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync("/messages", message, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            logger.LogWarning(
                "Orchestrator responded with non-success status {StatusCode} for message {MessageId}",
                response.StatusCode,
                message.MessageId);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to forward message {MessageId} to the Orchestrator after retries",
                message.MessageId);
            return false;
        }
    }
}
