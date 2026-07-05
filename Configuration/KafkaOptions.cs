namespace whatsapp_bff.Configuration;

public class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = string.Empty;
    public string MessageReceivedTopic { get; set; } = string.Empty;
    public string MessageStatusTopic { get; set; } = string.Empty;
}
