using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Application.UseCases;
using whatsapp_bff.Domain;
using Xunit;

namespace whatsapp_bff.Tests.Application.UseCases;

public class ProcessInboundWebhookUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ForwardSucceeds_PublishesMessageReceived()
    {
        var orchestrator = new Mock<IOrchestratorClient>();
        orchestrator
            .Setup(o => o.ForwardMessageAsync(It.IsAny<InboundChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var publisher = new Mock<IChannelEventPublisher>();
        var useCase = CreateUseCase(orchestrator.Object, publisher.Object, Mock.Of<IOutboundMessageTracker>());

        var allForwarded = await useCase.ExecuteAsync([TextMessage("wamid.bg-1")], [], CancellationToken.None);

        Assert.True(allForwarded);
        orchestrator.Verify(
            o => o.ForwardMessageAsync(It.Is<InboundChannelMessage>(m => m.MessageId == "wamid.bg-1"), It.IsAny<CancellationToken>()),
            Times.Once);
        publisher.Verify(
            p => p.PublishMessageReceivedAsync(It.Is<InboundChannelMessage>(m => m.MessageId == "wamid.bg-1"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardFails_DoesNotPublishMessageReceivedAndReturnsFalse()
    {
        var orchestrator = new Mock<IOrchestratorClient>();
        orchestrator
            .Setup(o => o.ForwardMessageAsync(It.IsAny<InboundChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var publisher = new Mock<IChannelEventPublisher>();
        var useCase = CreateUseCase(orchestrator.Object, publisher.Object, Mock.Of<IOutboundMessageTracker>());

        var allForwarded = await useCase.ExecuteAsync([TextMessage("wamid.bg-2")], [], CancellationToken.None);

        Assert.False(allForwarded);
        publisher.Verify(
            p => p.PublishMessageReceivedAsync(It.IsAny<InboundChannelMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_OneOfSeveralMessagesFailsToForward_ReturnsFalse()
    {
        var orchestrator = new Mock<IOrchestratorClient>();
        orchestrator
            .SetupSequence(o => o.ForwardMessageAsync(It.IsAny<InboundChannelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        var publisher = new Mock<IChannelEventPublisher>();
        var useCase = CreateUseCase(orchestrator.Object, publisher.Object, Mock.Of<IOutboundMessageTracker>());

        var allForwarded = await useCase.ExecuteAsync(
            [TextMessage("wamid.bg-2a"), TextMessage("wamid.bg-2b")], [], CancellationToken.None);

        Assert.False(allForwarded);
    }

    [Fact]
    public async Task ExecuteAsync_StatusEventKnownMessageId_PublishedAsKnown()
    {
        var tracker = new Mock<IOutboundMessageTracker>();
        tracker.Setup(t => t.IsKnown("wamid.out.1")).Returns(true);
        var publisher = new Mock<IChannelEventPublisher>();
        var useCase = CreateUseCase(Mock.Of<IOrchestratorClient>(), publisher.Object, tracker.Object);

        await useCase.ExecuteAsync([], [StatusEvent("wamid.out.1")], CancellationToken.None);

        publisher.Verify(
            p => p.PublishMessageStatusAsync(
                It.Is<MessageStatusEvent>(e => e.MessageId == "wamid.out.1" && e.IsKnownMessage),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_StatusEventUnknownMessageId_StillPublishedButMarkedUnknown()
    {
        var tracker = new Mock<IOutboundMessageTracker>();
        tracker.Setup(t => t.IsKnown(It.IsAny<string>())).Returns(false);
        var publisher = new Mock<IChannelEventPublisher>();
        var useCase = CreateUseCase(Mock.Of<IOrchestratorClient>(), publisher.Object, tracker.Object);

        await useCase.ExecuteAsync([], [StatusEvent("wamid.out.unknown")], CancellationToken.None);

        publisher.Verify(
            p => p.PublishMessageStatusAsync(
                It.Is<MessageStatusEvent>(e => e.MessageId == "wamid.out.unknown" && !e.IsKnownMessage),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ProcessInboundWebhookUseCase CreateUseCase(
        IOrchestratorClient orchestrator, IChannelEventPublisher publisher, IOutboundMessageTracker tracker) =>
        new(orchestrator, publisher, tracker, NullLogger<ProcessInboundWebhookUseCase>.Instance);

    private static InboundChannelMessage TextMessage(string messageId) => new()
    {
        MessageId = messageId,
        From = "5511999990000",
        ConversationId = "5511999990000",
        Type = ChannelMessageType.Text,
        Text = "hello",
        ReceivedAt = DateTimeOffset.UtcNow
    };

    private static MessageStatusEvent StatusEvent(string messageId) => new()
    {
        MessageId = messageId,
        ConversationId = "5511999990000",
        Status = MessageDeliveryStatus.Delivered,
        Timestamp = DateTimeOffset.UtcNow
    };
}
