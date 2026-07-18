namespace whatsapp_bff.Configuration;

public class OrchestratorOptions
{
    public const string SectionName = "Orchestrator";

    public string BaseUrl { get; set; } = string.Empty;
    public string TenantId { get; set; } = "00000000-0000-0000-0000-000000000001";
}
