using whatsapp_bff.Domain;

namespace whatsapp_bff.Application.Ports.Inbound;

public interface ISendOutboundMessageUseCase
{
    Task<SendOutboundMessageResult> ExecuteAsync(OutboundChannelMessage request, CancellationToken cancellationToken);
}

public record SendOutboundMessageResult(bool Success, string? MessageId, string? ErrorMessage);
