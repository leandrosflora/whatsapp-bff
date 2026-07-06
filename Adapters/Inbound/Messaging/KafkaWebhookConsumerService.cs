using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using whatsapp_bff.Adapters.Inbound.Http;
using whatsapp_bff.Adapters.Inbound.Http.Mapping;
using whatsapp_bff.Application.Ports.Inbound;
using whatsapp_bff.Configuration;

namespace whatsapp_bff.Adapters.Inbound.Messaging;

/// <summary>
/// Consumes raw WhatsApp webhook deliveries from Kafka - the durable queue between the webhook
/// endpoint and the Orchestrator, replacing what used to be an in-memory channel. The offset for
/// a delivery is only committed once every message in it was forwarded successfully; on failure
/// the consumer rewinds to that same offset and retries after a backoff, so an Orchestrator
/// outage applies backpressure on this topic instead of losing messages.
/// </summary>
public class KafkaWebhookConsumerService(
    IConsumer<string, string> consumer,
    IOptions<KafkaOptions> options,
    IWhatsAppPayloadMapper mapper,
    IProcessInboundWebhookUseCase useCase,
    ILogger<KafkaWebhookConsumerService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(2);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        consumer.Subscribe(options.Value.RawWebhookReceivedTopic);

        // Consume() is a blocking call, so it runs on a dedicated thread rather than the
        // ASP.NET Core thread pool used by async continuations elsewhere in the app.
        return Task.Run(() => RunLoop(stoppingToken), stoppingToken);
    }

    private void RunLoop(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                logger.LogError(ex, "Error consuming from {Topic}", options.Value.RawWebhookReceivedTopic);
                continue;
            }

            if (result?.Message is null)
            {
                continue;
            }

            if (ProcessMessageAsync(result, stoppingToken).GetAwaiter().GetResult())
            {
                consumer.Commit(result);
            }
            else
            {
                // Not committing alone would not replay this record - Consume() advances
                // regardless of commits - so we must explicitly rewind before retrying.
                logger.LogWarning(
                    "Forward to Orchestrator failed for {Topic}[{Partition}]@{Offset}; retrying the same offset in {BackoffSeconds}s",
                    result.Topic,
                    result.Partition.Value,
                    result.Offset.Value,
                    RetryBackoff.TotalSeconds);
                stoppingToken.WaitHandle.WaitOne(RetryBackoff);
                consumer.Seek(result.TopicPartitionOffset);
            }
        }

        consumer.Close();
    }

    /// <summary>Returns true if this offset is safe to commit: successfully processed, or an
    /// unrecoverable (poison) message that will never succeed no matter how many times it is retried.</summary>
    public async Task<bool> ProcessMessageAsync(ConsumeResult<string, string> result, CancellationToken cancellationToken)
    {
        var correlationId = result.Message.Headers is not null && result.Message.Headers.TryGetLastBytes("CorrelationId", out var bytes)
            ? System.Text.Encoding.UTF8.GetString(bytes)
            : Guid.NewGuid().ToString("n");

        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });

        WhatsAppWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WhatsAppWebhookPayload>(result.Message.Value);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse raw webhook payload read back from Kafka; dropping unparseable message");
            return true;
        }

        if (payload is null)
        {
            logger.LogWarning("Raw webhook payload from Kafka deserialized to null; dropping message");
            return true;
        }

        var messages = mapper.MapInboundMessages(payload, result.Message.Value);
        var statusEvents = mapper.MapStatusEvents(payload);

        var allForwarded = await useCase.ExecuteAsync(messages, statusEvents, cancellationToken);
        if (allForwarded)
        {
            logger.LogInformation(
                "Processed webhook delivery from Kafka: {MessageCount} message(s), {StatusCount} status event(s)",
                messages.Count,
                statusEvents.Count);
        }

        return allForwarded;
    }
}
