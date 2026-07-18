namespace whatsapp_bff.Configuration;

public class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = string.Empty;
    public string MessageReceivedTopic { get; set; } = string.Empty;
    public string MessageStatusTopic { get; set; } = string.Empty;
    public string RawWebhookReceivedTopic { get; set; } = string.Empty;
    public string RawWebhookRetryTopic { get; set; } = "channel.webhook.received.retry";
    public string RawWebhookDeadLetterTopic { get; set; } = "channel.webhook.received.dlq";
    public string WebhookConsumerGroupId { get; set; } = "whatsapp-bff-webhook-consumer";
    public int MaxDeliveryAttempts { get; set; } = 5;
    public int RetryBackoffSeconds { get; set; } = 2;
}
