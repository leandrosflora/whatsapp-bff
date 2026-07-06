using whatsapp_bff.Application.Ports.Inbound;
using whatsapp_bff.Domain;

namespace whatsapp_bff.Adapters.Inbound.Http;

public static class OutboundMessageEndpoints
{
    public static IEndpointRouteBuilder MapOutboundMessageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/internal/messages", HandleSendAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleSendAsync(
        OutboundChannelMessage request,
        ISendOutboundMessageUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.To) || string.IsNullOrWhiteSpace(request.Text))
        {
            return Results.BadRequest(new { error = "'to' and 'text' are required." });
        }

        var result = await useCase.ExecuteAsync(request, cancellationToken);

        return result.Success
            ? Results.Accepted(value: new { messageId = result.MessageId })
            : Results.Problem(detail: result.ErrorMessage, statusCode: StatusCodes.Status502BadGateway);
    }
}
