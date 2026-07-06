using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Configuration;

namespace whatsapp_bff.Adapters.Inbound.Http;

public static class WhatsAppWebhookEndpoints
{
    public static IEndpointRouteBuilder MapWhatsAppWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/webhooks/whatsapp");

        group.MapGet("", HandleVerificationAsync);
        group.MapPost("", HandleWebhookAsync);

        return endpoints;
    }

    private static IResult HandleVerificationAsync(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        IOptions<WhatsAppOptions> options)
    {
        var isValid = mode == "subscribe"
            && !string.IsNullOrEmpty(challenge)
            && verifyToken == options.Value.VerifyToken;

        return isValid
            ? Results.Text(challenge!, "text/plain")
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> HandleWebhookAsync(
        HttpRequest request,
        IOptions<WhatsAppOptions> whatsAppOptions,
        IMessageDedupeStore dedupeStore,
        IChannelEventPublisher eventPublisher,
        ILogger<InboundWebhookLogCategory> logger,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("n");
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });

        request.EnableBuffering();

        using var bodyStream = new MemoryStream();
        await request.Body.CopyToAsync(bodyStream, cancellationToken);
        var rawBytes = bodyStream.ToArray();
        request.Body.Position = 0;

        var signatureHeader = request.Headers["X-Hub-Signature-256"].ToString();
        if (!WebhookSignatureValidator.IsValid(rawBytes, signatureHeader, whatsAppOptions.Value.AppSecret))
        {
            logger.LogWarning("Rejected WhatsApp webhook delivery with invalid or missing signature");
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        WhatsAppWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WhatsAppWebhookPayload>(rawBytes);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse WhatsApp webhook payload");
            return Results.BadRequest();
        }

        if (payload is null)
        {
            return Results.BadRequest();
        }

        if (IsDuplicateDelivery(payload, dedupeStore))
        {
            logger.LogInformation("Dropped duplicate WhatsApp webhook delivery");
            return Results.Ok();
        }

        var rawJson = System.Text.Encoding.UTF8.GetString(rawBytes);

        try
        {
            var partitionKey = ResolvePartitionKey(payload, correlationId);
            await eventPublisher.PublishRawWebhookReceivedAsync(correlationId, partitionKey, rawJson, cancellationToken);
        }
        catch (Exception ex)
        {
            // The delivery was not durably recorded: reject it so the channel provider retries,
            // instead of acking a webhook that a crash could otherwise lose before a consumer
            // ever reads it back off the topic. Kafka is the queue now, not an in-memory buffer.
            logger.LogError(ex, "Failed to persist raw WhatsApp webhook delivery to Kafka; rejecting delivery");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        logger.LogInformation("Persisted raw webhook delivery to Kafka");

        return Results.Ok();
    }

    private static string ResolvePartitionKey(WhatsAppWebhookPayload payload, string fallback)
    {
        var messageFrom = payload.Entry
            .SelectMany(entry => entry.Changes)
            .Select(change => change.Value)
            .Where(value => value?.Messages is not null)
            .SelectMany(value => value!.Messages!)
            .Select(message => message.From)
            .FirstOrDefault(from => !string.IsNullOrEmpty(from));

        if (!string.IsNullOrEmpty(messageFrom))
        {
            return messageFrom;
        }

        var statusRecipient = payload.Entry
            .SelectMany(entry => entry.Changes)
            .Select(change => change.Value)
            .Where(value => value?.Statuses is not null)
            .SelectMany(value => value!.Statuses!)
            .Select(status => status.RecipientId)
            .FirstOrDefault(recipient => !string.IsNullOrEmpty(recipient));

        return !string.IsNullOrEmpty(statusRecipient) ? statusRecipient! : fallback;
    }

    private static bool IsDuplicateDelivery(WhatsAppWebhookPayload payload, IMessageDedupeStore dedupeStore)
    {
        var messageIds = payload.Entry
            .SelectMany(entry => entry.Changes)
            .Select(change => change.Value)
            .Where(value => value?.Messages is not null)
            .SelectMany(value => value!.Messages!)
            .Select(message => message.Id)
            .Where(id => id is not null)
            .Cast<string>()
            .ToList();

        if (messageIds.Count == 0)
        {
            return false;
        }

        // TryMarkProcessed has a side effect (marks as seen), so evaluate all before short-circuiting.
        var results = messageIds.Select(dedupeStore.TryMarkProcessed).ToList();
        return results.All(isNew => !isNew);
    }
}

public sealed class InboundWebhookLogCategory;
