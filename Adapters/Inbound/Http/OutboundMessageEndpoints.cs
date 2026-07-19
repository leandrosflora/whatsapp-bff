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
            return Results.Conflict(new
            {
                error = "Outbound delivery is already in progress or has an ambiguous outcome.",
                retryable = false,
                reconciliationRequired = true
            });
        }

        SendOutboundMessageResult result;
        try
        {
            result = await useCase.ExecuteAsync(request, cancellationToken);
        }
        catch
        {
            // Once the provider call started, its outcome may be ambiguous. Keep the Redis
            // reservation instead of allowing an automatic duplicate send.
            throw;
        }

        if (!result.Success || string.IsNullOrWhiteSpace(result.MessageId))
        {
            // Fail closed: the provider may have accepted the message even when the client
            // observed an error. The reservation expires only after the long pending TTL and
            // should normally be reconciled by an operator before that point.
            return Results.Json(
                new
                {
                    error = result.ErrorMessage ?? "Outbound delivery outcome is ambiguous.",
                    reconciliationRequired = true
                },
                statusCode: StatusCodes.Status502BadGateway);
        }

        try
        {
            await deliveryStore.CompleteAsync(
                tenantId,
                idempotencyKey,
                result.MessageId,
                cancellationToken);
        }
        catch
        {
            // Do not release: the WhatsApp API already returned a messageId. Releasing here
            // would permit the Outbox dispatcher to send the same customer reply again.
            throw;
        }

        return Results.Accepted(value: new { messageId = result.MessageId });
    }
}
