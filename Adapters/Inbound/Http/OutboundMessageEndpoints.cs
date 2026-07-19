using whatsapp_bff.Application.Ports.Inbound;
using whatsapp_bff.Application.Ports.Outbound;
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
        IOutboundDeliveryStore deliveryStore,
        CancellationToken cancellationToken)
    {
        if (!TenantClaims.Matches(
                httpContext.User,
                httpContext.Request.Headers["X-Tenant-Id"].ToString(),
                out var tenantId))
        {
            return Results.Json(
                new { error = "X-Tenant-Id must be a UUID and match the signed tenant_id claim." },
                statusCode: StatusCodes.Status403Forbidden);
        }
        if (string.IsNullOrWhiteSpace(request.To) || string.IsNullOrWhiteSpace(request.Text))
        {
            return Results.BadRequest(new { error = "'to' and 'text' are required." });
        }

        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { error = "Idempotency-Key header is required." });
        }

        var lease = await deliveryStore.TryAcquireAsync(
            tenantId,
            idempotencyKey,
            cancellationToken);
        if (lease.Status == OutboundDeliveryAcquireStatus.Completed)
        {
            return Results.Accepted(value: new { messageId = lease.MessageId, duplicate = true });
        }
        if (lease.Status == OutboundDeliveryAcquireStatus.InProgress)
        {
            return Results.Conflict(new { error = "Outbound delivery is already in progress.", retryable = true });
        }

        try
        {
            var result = await useCase.ExecuteAsync(request, cancellationToken);
            if (!result.Success || string.IsNullOrWhiteSpace(result.MessageId))
            {
                await deliveryStore.ReleaseAsync(tenantId, idempotencyKey, CancellationToken.None);
                return Results.Problem(
                    detail: result.ErrorMessage,
                    statusCode: StatusCodes.Status502BadGateway);
            }

            await deliveryStore.CompleteAsync(
                tenantId,
                idempotencyKey,
                result.MessageId,
                cancellationToken);
            return Results.Accepted(value: new { messageId = result.MessageId });
        }
        catch
        {
            await deliveryStore.ReleaseAsync(tenantId, idempotencyKey, CancellationToken.None);
            throw;
        }
    }
}
