using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using whatsapp_bff.Adapters.Outbound.Http;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Configuration;
using Xunit;

namespace whatsapp_bff.Tests.Adapters.Outbound.Http;

public class WhatsAppCloudApiClientTests
{
    [Fact]
    public async Task SendTypingIndicatorAsync_SuccessResponse_PostsExpectedBody()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(req =>
        {
            capturedRequest = req;
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = BuildClient(handler);

        await client.SendTypingIndicatorAsync("wamid.inbound-1", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("http://localhost/1245541268641087/messages", capturedRequest.RequestUri!.ToString());
        Assert.Contains("\"status\":\"read\"", capturedBody);
        Assert.Contains("\"message_id\":\"wamid.inbound-1\"", capturedBody);
        Assert.Contains("\"typing_indicator\":{\"type\":\"text\"}", capturedBody);
    }

    [Fact]
    public async Task SendTypingIndicatorAsync_ErrorResponse_DoesNotThrow()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"error":{"code":131030,"message":"Recipient phone number not in allowed list"}}""")
        });
        var client = BuildClient(handler);

        var exception = await Record.ExceptionAsync(
            () => client.SendTypingIndicatorAsync("wamid.inbound-2", CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendTypingIndicatorAsync_NetworkFailure_DoesNotThrow()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var client = BuildClient(handler);

        var exception = await Record.ExceptionAsync(
            () => client.SendTypingIndicatorAsync("wamid.inbound-3", CancellationToken.None));

        Assert.Null(exception);
    }

    private static IWhatsAppCloudApiClient BuildClient(StubHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<WhatsAppOptions>(o =>
        {
            o.PhoneNumberId = "1245541268641087";
            o.AccessToken = "test-access-token";
            o.GraphApiBaseUrl = "http://localhost/";
        });

        var httpClientBuilder = services.AddHttpClient<IWhatsAppCloudApiClient, WhatsAppCloudApiClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<WhatsAppOptions>>().Value;
            client.BaseAddress = new Uri(options.GraphApiBaseUrl);
        });
        httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => handler);

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IWhatsAppCloudApiClient>();
    }
}
