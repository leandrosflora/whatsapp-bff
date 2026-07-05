using Confluent.Kafka;
using Microsoft.Extensions.Options;
using whatsapp_bff.Configuration;
using whatsapp_bff.Events;
using whatsapp_bff.Mapping;
using whatsapp_bff.Orchestrator;
using whatsapp_bff.Outbound;
using whatsapp_bff.Processing;
using whatsapp_bff.Webhooks;

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
builder.Services.AddSingleton<IInboundWebhookQueue, InboundWebhookQueue>();
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

builder.Services.AddHostedService<InboundWebhookProcessingService>();

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
