using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using whatsapp_bff.Adapters.Inbound.Http;
using whatsapp_bff.Adapters.Inbound.Http.Mapping;
using whatsapp_bff.Application.Ports.Inbound;
using whatsapp_bff.Configuration;
using whatsapp_bff.Platform;

namespace whatsapp_bff.Adapters.Inbound.Messaging;

public class KafkaWebhookConsumerService(
    IConsumer<string, string> consumer,
    IProducer<string, string> producer,
    IOptions<KafkaOptions> options,
    IWhatsAppPayloadMapper mapper,
    IProcessInboundWebhookUseCase useCase,
    PlatformMetrics metrics,
    ILogger<KafkaWebhookConsumerService> logger) : BackgroundService
{
    public const string ActivitySourceName = "whatsapp-bff.kafka";
    private const string AttemptHeader = "x-delivery-attempt";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        consumer.Subscribe([
            options.Value.RawWebhookReceivedTopic,
            options.Value.RawWebhookRetryTopic
        ]);
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
                metrics.Increment("channel_kafka_consume_failures_total");
                logger.LogError(ex, "Error consuming webhook Kafka topics");
                continue;
            }

            if (result?.Message is null)
            {
                continue;
            }

            var processing = ProcessDeliveryAsync(result, stoppingToken).GetAwaiter().GetResult();
            if (processing.Success)
            {
                consumer.Commit(result);
                continue;
            }

            var currentAttempt = GetAttempt(result.Message.Headers);
            var mustDeadLetter = processing.Poison
                || currentAttempt >= Math.Max(1, options.Value.MaxDeliveryAttempts);

            var published = mustDeadLetter
                ? PublishDeadLetterAsync(result, processing.Reason, currentAttempt, stoppingToken)
                    .GetAwaiter().GetResult()
                : PublishRetryAsync(result, currentAttempt + 1, processing.Reason, stoppingToken)
                    .GetAwaiter().GetResult();

            if (published)
            {
                consumer.Commit(result);
            }
            else
            {
                logger.LogWarning(
                    "Could not persist retry/DLQ for {Topic}[{Partition}]@{Offset}; replaying original record",
                    result.Topic,
                    result.Partition.Value,
                    result.Offset.Value);
                consumer.Seek(result.TopicPartitionOffset);
            }
        }

        consumer.Close();
    }

    public async Task<bool> ProcessMessageAsync(
        ConsumeResult<string, string> result,
        CancellationToken cancellationToken) =>
        (await ProcessDeliveryAsync(result, cancellationToken)).Success;

    private async Task<ProcessingResult> ProcessDeliveryAsync(
        ConsumeResult<string, string> result,
        CancellationToken cancellationToken)
    {
        using var activity = StartConsumerActivity(result);
        var correlationId = ReadHeader(result.Message.Headers, "CorrelationId")
            ?? Guid.NewGuid().ToString("n");
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["KafkaTopic"] = result.Topic,
            ["KafkaPartition"] = result.Partition.Value,
            ["KafkaOffset"] = result.Offset.Value
        });

        WhatsAppWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WhatsAppWebhookPayload>(result.Message.Value);
        }
        catch (JsonException ex)
        {
            metrics.Increment("channel_webhook_poison_messages_total", ("reason", "invalid_json"));
            logger.LogError(ex, "Invalid webhook JSON read from Kafka; routing to DLQ");
            return ProcessingResult.PoisonMessage("invalid_json");
        }

        if (payload is null)
        {
            metrics.Increment("channel_webhook_poison_messages_total", ("reason", "null_payload"));
            return ProcessingResult.PoisonMessage("null_payload");
        }

        try
        {
            var messages = mapper.MapInboundMessages(payload, result.Message.Value);
            var statusEvents = mapper.MapStatusEvents(payload);
            var allForwarded = await useCase.ExecuteAsync(messages, statusEvents, cancellationToken);
            if (!allForwarded)
            {
                metrics.Increment("channel_webhook_processing_failures_total", ("reason", "orchestrator_rejected"));
                return ProcessingResult.Retryable("orchestrator_rejected");
            }

            metrics.Increment("channel_webhook_processed_total");
            logger.LogInformation(
                "Processed webhook delivery from Kafka: {MessageCount} message(s), {StatusCount} status event(s)",
                messages.Count,
                statusEvents.Count);
            return ProcessingResult.Completed();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            metrics.Increment("channel_webhook_processing_failures_total", ("reason", "processing_exception"));
            logger.LogError(ex, "Webhook processing failed; scheduling durable retry");
            return ProcessingResult.Retryable("processing_exception");
        }
    }

    private async Task<bool> PublishRetryAsync(
        ConsumeResult<string, string> result,
        int nextAttempt,
        string reason,
        CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(Math.Max(1, options.Value.RetryBackoffSeconds));
        await Task.Delay(backoff, cancellationToken);
        try
        {
            await producer.ProduceAsync(
                options.Value.RawWebhookRetryTopic,
                new Message<string, string>
                {
                    Key = result.Message.Key,
                    Value = result.Message.Value,
                    Headers = CopyHeaders(
                        result.Message.Headers,
                        (AttemptHeader, nextAttempt.ToString()),
                        ("retry-reason", reason))
                },
                cancellationToken);
            metrics.Increment("channel_webhook_retries_total", ("attempt", nextAttempt.ToString()));
            return true;
        }
        catch (Exception ex)
        {
            metrics.Increment("channel_webhook_retry_publish_failures_total");
            logger.LogError(ex, "Failed to publish webhook retry attempt {Attempt}", nextAttempt);
            return false;
        }
    }

    private async Task<bool> PublishDeadLetterAsync(
        ConsumeResult<string, string> result,
        string reason,
        int attempts,
        CancellationToken cancellationToken)
    {
        try
        {
            await producer.ProduceAsync(
                options.Value.RawWebhookDeadLetterTopic,
                new Message<string, string>
                {
                    Key = result.Message.Key,
                    Value = result.Message.Value,
                    Headers = CopyHeaders(
                        result.Message.Headers,
                        (AttemptHeader, attempts.ToString()),
                        ("dead-letter-reason", reason),
                        ("source-topic", result.Topic),
                        ("source-partition", result.Partition.Value.ToString()),
                        ("source-offset", result.Offset.Value.ToString()))
                },
                cancellationToken);
            metrics.Increment("channel_webhook_dead_letter_total", ("reason", reason));
            logger.LogError(
                "Webhook moved to DLQ after {Attempts} attempt(s): reason={Reason}",
                attempts,
                reason);
            return true;
        }
        catch (Exception ex)
        {
            metrics.Increment("channel_webhook_dlq_publish_failures_total");
            logger.LogError(ex, "Failed to publish webhook to DLQ");
            return false;
        }
    }

    private static Activity? StartConsumerActivity(ConsumeResult<string, string> result)
    {
        var traceParent = ReadHeader(result.Message.Headers, "traceparent");
        var traceState = ReadHeader(result.Message.Headers, "tracestate");
        ActivityContext parent = default;
        if (!string.IsNullOrWhiteSpace(traceParent))
        {
            ActivityContext.TryParse(traceParent, traceState, out parent);
        }

        var activity = ActivitySource.StartActivity(
            $"{result.Topic} consume",
            ActivityKind.Consumer,
            parent);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", result.Topic);
        activity?.SetTag("messaging.kafka.partition", result.Partition.Value);
        activity?.SetTag("messaging.kafka.offset", result.Offset.Value);
        return activity;
    }

    private static int GetAttempt(Headers? headers) =>
        int.TryParse(ReadHeader(headers, AttemptHeader), out var attempt) && attempt > 0 ? attempt : 1;

    private static string? ReadHeader(Headers? headers, string name)
    {
        if (headers is null || !headers.TryGetLastBytes(name, out var value) || value is null)
        {
            return null;
        }
        return Encoding.UTF8.GetString(value);
    }

    private static Headers CopyHeaders(Headers? source, params (string Name, string Value)[] replacements)
    {
        var replacementNames = replacements.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var headers = new Headers();
        if (source is not null)
        {
            foreach (var header in source)
            {
                if (!replacementNames.Contains(header.Key))
                {
                    headers.Add(header.Key, header.GetValueBytes());
                }
            }
        }
        foreach (var replacement in replacements)
        {
            headers.Add(replacement.Name, Encoding.UTF8.GetBytes(replacement.Value));
        }
        return headers;
    }

    private sealed record ProcessingResult(bool Success, bool Poison, string Reason)
    {
        public static ProcessingResult Completed() => new(true, false, "completed");
        public static ProcessingResult Retryable(string reason) => new(false, false, reason);
        public static ProcessingResult PoisonMessage(string reason) => new(false, true, reason);
    }
}
