using Confluent.Kafka;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using whatsapp_bff.Adapters.Inbound.Http;
using whatsapp_bff.Adapters.Inbound.Http.Mapping;
using whatsapp_bff.Adapters.Inbound.Messaging;
using whatsapp_bff.Adapters.Outbound.Http;
using whatsapp_bff.Adapters.Outbound.Messaging;
using whatsapp_bff.Adapters.Outbound.Persistence;
using whatsapp_bff.Application.Ports.Inbound;
using whatsapp_bff.Application.Ports.Outbound;
using whatsapp_bff.Application.UseCases;
using whatsapp_bff.Configuration;
using whatsapp_bff.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddPlatformServices(builder.Configuration);

builder.Logging.Configure(options =>
{
    options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId
        | ActivityTrackingOptions.SpanId
        | ActivityTrackingOptions.ParentId;
});
builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true);

builder.Services.AddOptions<WhatsAppOptions>()
    .Bind(builder.Configuration.GetSection(WhatsAppOptions.SectionName));
builder.Services.AddOptions<OrchestratorOptions>()
    .Bind(builder.Configuration.GetSection(OrchestratorOptions.SectionName));
builder.Services.AddOptions<KafkaOptions>()
    .Bind(builder.Configuration.GetSection(KafkaOptions.SectionName));
builder.Services.AddOptions<OtelOptions>()
    .Bind(builder.Configuration.GetSection(OtelOptions.SectionName));

var otelEndpoint = builder.Configuration.GetSection(OtelOptions.SectionName).Get<OtelOptions>()?.OtlpEndpoint
    ?? "http://localhost:4317";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("whatsapp-bff"))
    .WithTracing(tracing => tracing
        .AddSource(KafkaWebhookConsumerService.ActivitySourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otelEndpoint)));

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IMessageDedupeStore, MemoryCacheMessageDedupeStore>();
builder.Services.AddSingleton<IWhatsAppPayloadMapper, WhatsAppPayloadMapper>();
builder.Services.AddSingleton<IOutboundMessageTracker, InMemoryOutboundMessageTracker>();

builder.Services.AddHttpClient<IOrchestratorClient, OrchestratorClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<OrchestratorOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
    })
    .AddHttpMessageHandler(sp => new InternalAuthHandler(
        sp.GetRequiredService<InternalTokenService>(),
        sp.GetRequiredService<IOptions<OrchestratorOptions>>(),
        "conversation-orchestrator"))
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
        options.Retry.DisableForUnsafeHttpMethods();
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(35);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
    });

builder.Services.AddHttpClient<IWhatsAppCloudApiClient, WhatsAppCloudApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WhatsAppOptions>>().Value;
    client.BaseAddress = new Uri(options.GraphApiBaseUrl.TrimEnd('/') + "/");
});

builder.Services.AddSingleton<IProducer<string, string>>(sp =>
{
    var options = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;
    return new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = options.BootstrapServers,
        EnableIdempotence = true,
        Acks = Acks.All
    }).Build();
});
builder.Services.AddSingleton<IChannelEventPublisher, KafkaChannelEventPublisher>();

builder.Services.AddSingleton<IConsumer<string, string>>(sp =>
{
    var options = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;
    return new ConsumerBuilder<string, string>(new ConsumerConfig
    {
        BootstrapServers = options.BootstrapServers,
        GroupId = options.WebhookConsumerGroupId,
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false,
        AllowAutoCreateTopics = false
    }).Build();
});
builder.Services.AddSingleton<IAdminClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;
    return new AdminClientBuilder(new AdminClientConfig
    {
        BootstrapServers = options.BootstrapServers
    }).Build();
});

builder.Services.AddTransient<IProcessInboundWebhookUseCase, ProcessInboundWebhookUseCase>();
builder.Services.AddTransient<ISendOutboundMessageUseCase, SendOutboundMessageUseCase>();
builder.Services.AddHostedService<KafkaWebhookConsumerService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UsePlatformServices();
app.MapPlatformEndpoints();
app.MapGet("/health/ready", (
    IAdminClient adminClient,
    IOptions<InternalAuthOptions> authOptions,
    IOptions<OrchestratorOptions> orchestratorOptions) =>
{
    var failures = new List<string>();
    if (Encoding.UTF8.GetByteCount(authOptions.Value.SigningKey) < 32)
    {
        failures.Add("internal_auth_signing_key_invalid");
    }
    if (!TenantClaims.TryNormalize(orchestratorOptions.Value.TenantId, out _))
    {
        failures.Add("channel_tenant_invalid");
    }
    try
    {
        adminClient.GetMetadata(TimeSpan.FromSeconds(2));
    }
    catch
    {
        failures.Add("kafka_unavailable");
    }

    return failures.Count == 0
        ? Results.Ok(new { status = "ready", failures })
        : Results.Json(new { status = "not_ready", failures }, statusCode: StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapWhatsAppWebhookEndpoints();
app.MapOutboundMessageEndpoints();

app.Run();

public partial class Program;
