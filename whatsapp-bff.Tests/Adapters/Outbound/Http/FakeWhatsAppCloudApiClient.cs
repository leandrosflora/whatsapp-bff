using System.Collections.Concurrent;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Domain;

namespace whatsapp_bff.Tests.Adapters.Outbound.Http;

/// <summary>
/// Records calls for assertions instead of talking to the real WhatsApp Cloud API.
/// </summary>
public class FakeWhatsAppCloudApiClient : IWhatsAppCloudApiClient
{
    public ConcurrentQueue<string> TypingIndicatorMessageIds { get; } = new();
    public ConcurrentQueue<(string To, string BodyText, IReadOnlyList<OutboundButton> Buttons)> InteractiveSends { get; } = new();

    public bool ThrowOnTypingIndicator { get; set; }

    public Task<WhatsAppSendResult> SendTextMessageAsync(string to, string text, CancellationToken cancellationToken) =>
        Task.FromResult(new WhatsAppSendResult(true, "wamid.fake", null, null));

    public Task<WhatsAppSendResult> SendInteractiveButtonsAsync(
        string to, string bodyText, IReadOnlyList<OutboundButton> buttons, CancellationToken cancellationToken)
    {
        InteractiveSends.Enqueue((to, bodyText, buttons));
        return Task.FromResult(new WhatsAppSendResult(true, "wamid.fake-interactive", null, null));
    }

    public Task SendTypingIndicatorAsync(string incomingMessageId, CancellationToken cancellationToken)
    {
        if (ThrowOnTypingIndicator)
        {
            throw new InvalidOperationException("Simulated WhatsApp Cloud API failure");
        }

        TypingIndicatorMessageIds.Enqueue(incomingMessageId);
        return Task.CompletedTask;
    }
}
