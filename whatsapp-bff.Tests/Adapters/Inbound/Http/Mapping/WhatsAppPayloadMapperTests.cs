using System.Text.Json;
using whatsapp_bff.Adapters.Inbound.Http.Mapping;
using whatsapp_bff.Domain;
using whatsapp_bff.Adapters.Inbound.Http;
using Xunit;

namespace whatsapp_bff.Tests.Adapters.Inbound.Http.Mapping;

public class WhatsAppPayloadMapperTests
{
    private readonly WhatsAppPayloadMapper _mapper = new();

    [Fact]
    public void MapInboundMessages_TextMessage_ProducesTextCanonicalMessage()
    {
        const string json = """
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "entry-1",
            "changes": [{
              "field": "messages",
              "value": {
                "messaging_product": "whatsapp",
                "messages": [{
                  "id": "wamid.123",
                  "from": "5511999990000",
                  "timestamp": "1700000000",
                  "type": "text",
                  "text": { "body": "Olá, quero renegociar minha dívida" }
                }]
              }
            }]
          }]
        }
        """;

        var payload = Deserialize(json);

        var messages = _mapper.MapInboundMessages(payload, json);

        var message = Assert.Single(messages);
        Assert.Equal("wamid.123", message.MessageId);
        Assert.Equal("5511999990000", message.From);
        Assert.Equal("5511999990000", message.ConversationId);
        Assert.Equal(ChannelMessageType.Text, message.Type);
        Assert.Equal("Olá, quero renegociar minha dívida", message.Text);
        Assert.Null(message.Interactive);
    }

    [Fact]
    public void MapInboundMessages_InteractiveButtonReply_ProducesInteractiveCanonicalMessage()
    {
        const string json = """
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "entry-1",
            "changes": [{
              "field": "messages",
              "value": {
                "messages": [{
                  "id": "wamid.456",
                  "from": "5511999990000",
                  "timestamp": "1700000001",
                  "type": "interactive",
                  "interactive": {
                    "type": "button_reply",
                    "button_reply": { "id": "opt-accept", "title": "Aceitar proposta" }
                  }
                }]
              }
            }]
          }]
        }
        """;

        var payload = Deserialize(json);

        var messages = _mapper.MapInboundMessages(payload, json);

        var message = Assert.Single(messages);
        Assert.Equal(ChannelMessageType.Interactive, message.Type);
        Assert.NotNull(message.Interactive);
        Assert.Equal("opt-accept", message.Interactive!.Id);
        Assert.Equal("Aceitar proposta", message.Interactive!.Title);
    }

    [Fact]
    public void MapInboundMessages_UnsupportedType_PreservesRawPayloadAndDoesNotThrow()
    {
        const string json = """
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "entry-1",
            "changes": [{
              "field": "messages",
              "value": {
                "messages": [{
                  "id": "wamid.789",
                  "from": "5511999990000",
                  "timestamp": "1700000002",
                  "type": "sticker"
                }]
              }
            }]
          }]
        }
        """;

        var payload = Deserialize(json);

        var messages = _mapper.MapInboundMessages(payload, json);

        var message = Assert.Single(messages);
        Assert.Equal(ChannelMessageType.Unsupported, message.Type);
        Assert.NotNull(message.RawPayload);
    }

    [Fact]
    public void MapStatusEvents_DeliveredStatus_ProducesDeliveredCanonicalEvent()
    {
        const string json = """
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "entry-1",
            "changes": [{
              "field": "messages",
              "value": {
                "statuses": [{
                  "id": "wamid.out.1",
                  "status": "delivered",
                  "timestamp": "1700000010",
                  "recipient_id": "5511999990000"
                }]
              }
            }]
          }]
        }
        """;

        var payload = Deserialize(json);

        var events = _mapper.MapStatusEvents(payload);

        var statusEvent = Assert.Single(events);
        Assert.Equal("wamid.out.1", statusEvent.MessageId);
        Assert.Equal(MessageDeliveryStatus.Delivered, statusEvent.Status);
        Assert.Null(statusEvent.Error);
    }

    [Fact]
    public void MapStatusEvents_FailedStatus_IncludesErrorDetails()
    {
        const string json = """
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "entry-1",
            "changes": [{
              "field": "messages",
              "value": {
                "statuses": [{
                  "id": "wamid.out.2",
                  "status": "failed",
                  "timestamp": "1700000011",
                  "recipient_id": "5511999990000",
                  "errors": [{ "code": 131056, "title": "Rate limit", "message": "Too many messages" }]
                }]
              }
            }]
          }]
        }
        """;

        var payload = Deserialize(json);

        var events = _mapper.MapStatusEvents(payload);

        var statusEvent = Assert.Single(events);
        Assert.Equal(MessageDeliveryStatus.Failed, statusEvent.Status);
        Assert.NotNull(statusEvent.Error);
        Assert.Equal("131056", statusEvent.Error!.Code);
        Assert.Equal("Too many messages", statusEvent.Error!.Message);
    }

    private static WhatsAppWebhookPayload Deserialize(string json) =>
        JsonSerializer.Deserialize<WhatsAppWebhookPayload>(json)!;
}
