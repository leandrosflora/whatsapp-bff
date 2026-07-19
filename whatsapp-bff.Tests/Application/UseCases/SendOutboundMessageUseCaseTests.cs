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

    private static SendOutboundMessageUseCase CreateUseCase(
        IWhatsAppCloudApiClient whatsAppClient, IOutboundMessageTracker tracker, IChannelEventPublisher publisher) =>
        new(whatsAppClient, tracker, publisher, NullLogger<SendOutboundMessageUseCase>.Instance);
}
