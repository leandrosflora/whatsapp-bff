namespace whatsapp_bff.Configuration;

public class OtelOptions
{
    public const string SectionName = "Otel";

    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
}
