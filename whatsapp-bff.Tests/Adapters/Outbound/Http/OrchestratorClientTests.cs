using System.Net;
using Microsoft.Extensions.DependencyInjection;
using whatsapp_bff.Domain;
using whatsapp_bff.Adapters.Outbound.Http;
using whatsapp_bff.Application.Ports.Outbound;
using Xunit;

namespace whatsapp_bff.Tests.Adapters.Outbound.Http;

public class OrchestratorClientTests
{
    [Fact]
    public async Task ForwardMessageAsync_SuccessResponse_ReturnsTrue()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = BuildClient(handler);

        var result = await client.ForwardMessageAsync(SampleMessage(), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ForwardMessageAsync_TransientFailureThenSuccess_RetriesAndReturnsTrue()
    {
        var handler = new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = BuildClient(handler, maxRetryAttempts: 2);

        var result = await client.ForwardMessageAsync(SampleMessage(), CancellationToken.None);

        Assert.True(result);
        Assert.True(handler.CallCount >= 2);
    }

    [Fact]
    public async Task ForwardMessageAsync_OrchestratorUnreachable_ReturnsFalseWithoutThrowing()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var client = BuildClient(handler);

        var result = await client.ForwardMessageAsync(SampleMessage(), CancellationToken.None);

        Assert.False(result);
    }

    private static IOrchestratorClient BuildClient(StubHttpMessageHandler handler, int maxRetryAttempts = 0)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var httpClientBuilder = services.AddHttpClient<IOrchestratorClient, OrchestratorClient>(client =>
        {
            client.BaseAddress = new Uri("http://localhost/");
        });
        httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => handler);

        if (maxRetryAttempts > 0)
        {
            httpClientBuilder.AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = maxRetryAttempts;
                options.Retry.Delay = TimeSpan.FromMilliseconds(10);
                options.Retry.BackoffType = Polly.DelayBackoffType.Constant;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.CircuitBreaker.MinimumThroughput = int.MaxValue;
            });
        }

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOrchestratorClient>();
    }

    private static InboundChannelMessage SampleMessage() => new()
    {
        MessageId = "wamid.1",
        From = "5511999990000",
        ConversationId = "5511999990000",
        Type = ChannelMessageType.Text,
        Text = "hello",
        ReceivedAt = DateTimeOffset.UtcNow
    };
}

internal class StubHttpMessageHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responders) : HttpMessageHandler
{
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var responder = responders[Math.Min(CallCount, responders.Length - 1)];
        CallCount++;
        return Task.FromResult(responder(request));
    }
}
