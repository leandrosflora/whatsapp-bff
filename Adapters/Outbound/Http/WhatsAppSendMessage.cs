using System.Text.Json.Serialization;

namespace whatsapp_bff.Adapters.Outbound.Http;

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

public class WhatsAppSendInteractiveRequest
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; init; } = "whatsapp";

    [JsonPropertyName("to")]
    public required string To { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "interactive";

    [JsonPropertyName("interactive")]
    public required WhatsAppInteractivePayload Interactive { get; init; }
}

public class WhatsAppInteractivePayload
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "button";

    [JsonPropertyName("body")]
    public required WhatsAppInteractiveBody Body { get; init; }

    [JsonPropertyName("action")]
    public required WhatsAppInteractiveAction Action { get; init; }
}

public class WhatsAppInteractiveBody
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

public class WhatsAppInteractiveAction
{
    [JsonPropertyName("buttons")]
    public required List<WhatsAppInteractiveButton> Buttons { get; init; }
}

public class WhatsAppInteractiveButton
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "reply";

    [JsonPropertyName("reply")]
    public required WhatsAppInteractiveButtonReply Reply { get; init; }
}

public class WhatsAppInteractiveButtonReply
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }
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

public class WhatsAppTypingIndicatorRequest
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; init; } = "whatsapp";

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("message_id")]
    public required string MessageId { get; init; }

    [JsonPropertyName("typing_indicator")]
    public required WhatsAppTypingIndicatorType TypingIndicator { get; init; }
}

public class WhatsAppTypingIndicatorType
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";
}

public class WhatsAppErrorDetail
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
