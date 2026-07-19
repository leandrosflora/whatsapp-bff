using whatsapp_bff.Application.Ports.Inbound;
using whatsapp_bff.Domain;
using whatsapp_bff.Platform;

namespace whatsapp_bff.Adapters.Inbound.Http;

public static class OutboundMessageEndpoints
{
    public static IEndpointRouteBuilder MapOutboundMessageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/internal/messages", HandleSendAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> HandleSendAsync(
        HttpContext httpContext,
        OutboundChannelMessage request,
        ISendOutboundMessageUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (!TenantClaims.Matches(
                httpContext.User,
                httpContext.Request.Headers["X-Tenant-Id"].ToString(),
                out _))
        {
            return Results.Json(
                new { error = "X-Tenant-Id must be a UUID and match the signed tenant_id claim." },
                statusCode: StatusCodes.Status403Forbidden);
        }
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
