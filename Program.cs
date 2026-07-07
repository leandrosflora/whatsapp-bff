using Confluent.Kafka;
using Microsoft.Extensions.Options;
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

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Attach distributed-trace IDs to every log scope and render scopes in console output,
// so a single WhatsApp webhook delivery can be correlated across ingestion, Orchestrator
// calls, Kafka publishing, and outbound send.
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

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IMessageDedupeStore, MemoryCacheMessageDedupeStore>();
builder.Services.AddSingleton<IWhatsAppPayloadMapper, WhatsAppPayloadMapper>();
builder.Services.AddSingleton<IOutboundMessageTracker, InMemoryOutboundMessageTracker>();

builder.Services.AddHttpClient<IOrchestratorClient, OrchestratorClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<OrchestratorOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
    })
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
    });

builder.Services.AddHttpClient<IWhatsAppCloudApiClient, WhatsAppCloudApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WhatsAppOptions>>().Value;
    client.BaseAddress = new Uri(options.GraphApiBaseUrl.TrimEnd('/') + "/");
});

builder.Services.AddSingleton<IProducer<string, string>>(sp =>
{
    var options = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;
    var config = new ProducerConfig { BootstrapServers = options.BootstrapServers };
    return new ProducerBuilder<string, string>(config).Build();
});
builder.Services.AddSingleton<IChannelEventPublisher, KafkaChannelEventPublisher>();

builder.Services.AddSingleton<IConsumer<string, string>>(sp =>
{
    var options = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;
    var config = new ConsumerConfig
    {
        BootstrapServers = options.BootstrapServers,
        GroupId = options.WebhookConsumerGroupId,
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false,
        // librdkafka defaults this to false regardless of the broker's own
        // auto.create.topics.enable, so a consumer started before any producer
        // has touched the topic fails with "Unknown topic or partition" instead
        // of creating it.
        AllowAutoCreateTopics = true
    };
    return new ConsumerBuilder<string, string>(config).Build();
});

// Transient (not Scoped): ProcessInboundWebhookUseCase is consumed by the singleton
// KafkaWebhookConsumerService BackgroundService, which cannot depend on a scoped service.
builder.Services.AddTransient<IProcessInboundWebhookUseCase, ProcessInboundWebhookUseCase>();
builder.Services.AddTransient<ISendOutboundMessageUseCase, SendOutboundMessageUseCase>();

builder.Services.AddHostedService<KafkaWebhookConsumerService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapWhatsAppWebhookEndpoints();
app.MapOutboundMessageEndpoints();

app.Run();

public partial class Program;
