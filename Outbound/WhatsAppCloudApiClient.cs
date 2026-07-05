using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using whatsapp_bff.Configuration;
using whatsapp_bff.Models.WhatsApp;

namespace whatsapp_bff.Outbound;

public class WhatsAppCloudApiClient(
    HttpClient httpClient,
    IOptions<WhatsAppOptions> options,
    ILogger<WhatsAppCloudApiClient> logger) : IWhatsAppCloudApiClient
{
    public async Task<WhatsAppSendResult> SendTextMessageAsync(string to, string text, CancellationToken cancellationToken)
    {
        var whatsAppOptions = options.Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{whatsAppOptions.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(new WhatsAppSendMessageRequest
            {
                To = to,
                Type = "text",
                Text = new WhatsAppSendMessageText { Body = text }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", whatsAppOptions.AccessToken);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var parsed = JsonSerializer.Deserialize<WhatsAppSendMessageResponse>(body);
                var messageId = parsed?.Messages?.FirstOrDefault()?.Id;

                return messageId is not null
                    ? new WhatsAppSendResult(true, messageId, null, null)
                    : new WhatsAppSendResult(false, null, "invalid_response", "WhatsApp API response did not contain a message ID.");
            }

            var error = TryParseError(body);
            logger.LogWarning("WhatsApp Cloud API rejected outbound message to {To}: {Error}", to, error?.Message);

            return new WhatsAppSendResult(
                false,
                null,
                error?.Code.ToString() ?? ((int)response.StatusCode).ToString(),
                error?.Message ?? "WhatsApp API request failed.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to call WhatsApp Cloud API for outbound message to {To}", to);
            return new WhatsAppSendResult(false, null, "request_failed", ex.Message);
        }
    }

    private static WhatsAppErrorDetail? TryParseError(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<WhatsAppErrorResponse>(body)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
