namespace whatsapp_bff.Configuration;

public class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    public string PhoneNumberId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string VerifyToken { get; set; } = string.Empty;
    public string GraphApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Redacts secrets so accidental logging of the options object (e.g. structured "{@Options}") never leaks them.</summary>
    public override string ToString() =>
        $"{nameof(WhatsAppOptions)} {{ PhoneNumberId = {PhoneNumberId}, GraphApiBaseUrl = {GraphApiBaseUrl}, " +
        "AccessToken = [REDACTED], AppSecret = [REDACTED], VerifyToken = [REDACTED] }";
}
