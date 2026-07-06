using System.Text.Json.Serialization;

namespace whatsapp_bff.Adapters.Inbound.Http;

public class WhatsAppWebhookPayload
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("entry")]
    public List<WhatsAppEntry> Entry { get; set; } = [];
}

public class WhatsAppEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("changes")]
    public List<WhatsAppChange> Changes { get; set; } = [];
}

public class WhatsAppChange
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("value")]
    public WhatsAppChangeValue? Value { get; set; }
}

public class WhatsAppChangeValue
{
    [JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; set; }

    [JsonPropertyName("metadata")]
    public WhatsAppMetadata? Metadata { get; set; }

    [JsonPropertyName("contacts")]
    public List<WhatsAppContact>? Contacts { get; set; }

    [JsonPropertyName("messages")]
    public List<WhatsAppMessage>? Messages { get; set; }

    [JsonPropertyName("statuses")]
    public List<WhatsAppStatus>? Statuses { get; set; }
}

public class WhatsAppMetadata
{
    [JsonPropertyName("display_phone_number")]
    public string? DisplayPhoneNumber { get; set; }

    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; set; }
}

public class WhatsAppContact
{
    [JsonPropertyName("wa_id")]
    public string? WaId { get; set; }

    [JsonPropertyName("profile")]
    public WhatsAppProfile? Profile { get; set; }
}

public class WhatsAppProfile
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class WhatsAppMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public WhatsAppTextBody? Text { get; set; }

    [JsonPropertyName("interactive")]
    public WhatsAppInteractive? Interactive { get; set; }
}

public class WhatsAppTextBody
{
    [JsonPropertyName("body")]
    public string? Body { get; set; }
}

public class WhatsAppInteractive
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("button_reply")]
    public WhatsAppInteractiveOption? ButtonReply { get; set; }

    [JsonPropertyName("list_reply")]
    public WhatsAppInteractiveOption? ListReply { get; set; }
}

public class WhatsAppInteractiveOption
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

public class WhatsAppStatus
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("recipient_id")]
    public string? RecipientId { get; set; }

    [JsonPropertyName("errors")]
    public List<WhatsAppError>? Errors { get; set; }
}

public class WhatsAppError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
