using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Application.UseCases;
using whatsapp_bff.Domain;
using Xunit;

namespace whatsapp_bff.Tests.Application.UseCases;

public class SendOutboundMessageUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_Success_MarksTrackerSentAndDoesNotPublishStatus()
    {
        var whatsAppClient = new Mock<IWhatsAppCloudApiClient>();
        whatsAppClient
            .Setup(c => c.SendTextMessageAsync("5511999990000", "hello", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppSendResult(true, "wamid.sent-1", null, null));
        var tracker = new Mock<IOutboundMessageTracker>();
        var publisher = new Mock<IChannelEventPublisher>();
        var useCase = CreateUseCase(whatsAppClient.Object, tracker.Object, publisher.Object);

        var result = await useCase.ExecuteAsync(
            new OutboundChannelMessage { To = "5511999990000", Type = "text", Text = "hello" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("wamid.sent-1", result.MessageId);
        tracker.Verify(t => t.MarkSent("wamid.sent-1"), Times.Once);
        publisher.Verify(
            p => p.PublishMessageStatusAsync(It.IsAny<MessageStatusEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhatsAppApiFails_ReturnsFailureAndPublishesFailedStatus()
    {
        var whatsAppClient = new Mock<IWhatsAppCloudApiClient>();
        whatsAppClient
            .Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppSendResult(false, null, "131056", "rate limited"));
        var publisher = new Mock<IChannelEventPublisher>();
        var useCase = CreateUseCase(whatsAppClient.Object, Mock.Of<IOutboundMessageTracker>(), publisher.Object);

        var result = await useCase.ExecuteAsync(
            new OutboundChannelMessage { To = "5511999990000", Type = "text", Text = "hello" }, CancellationToken.None);

        Assert.False(result.Success);
        publisher.Verify(
            p => p.PublishMessageStatusAsync(
                It.Is<MessageStatusEvent>(e => e.Status == MessageDeliveryStatus.Failed && e.Error!.Code == "131056"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_InteractiveType_CallsSendInteractiveButtonsNotSendText()
    {
        var whatsAppClient = new Mock<IWhatsAppCloudApiClient>();
        var buttons = new List<OutboundButton> { new("skill_a", "Opção A"), new("skill_b", "Opção B") };
        whatsAppClient
            .Setup(c => c.SendInteractiveButtonsAsync(
                "5511999990000", "Escolha uma opção", buttons, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppSendResult(true, "wamid.menu-1", null, null));
        var tracker = new Mock<IOutboundMessageTracker>();
        var useCase = CreateUseCase(whatsAppClient.Object, tracker.Object, Mock.Of<IChannelEventPublisher>());

        var result = await useCase.ExecuteAsync(
            new OutboundChannelMessage
            {
                To = "5511999990000",
                Type = "interactive",
                Text = "Escolha uma opção",
                Buttons = buttons
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("wamid.menu-1", result.MessageId);
        tracker.Verify(t => t.MarkSent("wamid.menu-1"), Times.Once);
        whatsAppClient.Verify(
            c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static SendOutboundMessageUseCase CreateUseCase(
        IWhatsAppCloudApiClient whatsAppClient, IOutboundMessageTracker tracker, IChannelEventPublisher publisher) =>
        new(whatsAppClient, tracker, publisher, NullLogger<SendOutboundMessageUseCase>.Instance);
}
