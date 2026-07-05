using System.Text.Json.Serialization;

namespace whatsapp_bff.Models.WhatsApp;

public class WhatsAppSendMessageRequest
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; init; } = "whatsapp";

    [JsonPropertyName("to")]
    public required string To { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    public WhatsAppSendMessageText? Text { get; init; }
}

public class WhatsAppSendMessageText
{
    [JsonPropertyName("body")]
    public required string Body { get; init; }
}

public class WhatsAppSendMessageResponse
{
    [JsonPropertyName("messages")]
    public List<WhatsAppSentMessageId>? Messages { get; set; }
}

public class WhatsAppSentMessageId
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

public class WhatsAppErrorResponse
{
    [JsonPropertyName("error")]
    public WhatsAppErrorDetail? Error { get; set; }
}

public class WhatsAppErrorDetail
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
