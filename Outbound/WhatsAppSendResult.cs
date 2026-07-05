namespace whatsapp_bff.Outbound;

public record WhatsAppSendResult(bool Success, string? WhatsAppMessageId, string? ErrorCode, string? ErrorMessage);
