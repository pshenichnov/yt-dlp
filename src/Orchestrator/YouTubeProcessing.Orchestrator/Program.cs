using MassTransit;
using Microsoft.EntityFrameworkCore;
using YouTubeProcessing.Contracts;
using YouTubeProcessing.Observability;
using YouTubeProcessing.Orchestrator;
using YouTubeProcessing.Orchestrator.Consumers;
using YouTubeProcessing.Orchestrator.Data;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services.AddDbContext<OrchestratorDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrchestratorDatabase")));
builder.Services.AddOptions<WorkflowOptions>()
    .Bind(builder.Configuration.GetSection(WorkflowOptions.SectionName));
builder.Services.AddHostedService<WorkflowTimeoutService>();
builder.Services.AddPlatformObservability(builder.Configuration, "youtube-processing-orchestrator");

builder.Services.AddMassTransit(configurator =>
{
    configurator.SetKebabCaseEndpointNameFormatter();
    configurator.AddConsumer<VideoAnalysisRequestedConsumer>();
    configurator.AddConsumer<ExtractorEventConsumer>();
    configurator.AddConsumer<TranscriptAnalyzedConsumer>();
    configurator.AddConsumer<TranscriptAnalysisFailedConsumer>();
    configurator.AddEntityFrameworkOutbox<OrchestratorDbContext>(outbox =>
    {
        outbox.UsePostgres();
        outbox.UseBusOutbox();
    });

    configurator.UsingRabbitMq((context, rabbit) =>
    {
        var rabbitSettings = builder.Configuration.GetSection("RabbitMq");
        rabbit.Host(
            rabbitSettings["Host"] ?? "localhost",
            rabbitSettings["VirtualHost"] ?? "/",
            host =>
            {
                host.Username(rabbitSettings["Username"] ?? "guest");
                host.Password(rabbitSettings["Password"] ?? "guest");
            });

        rabbit.AddRawJsonSerializer(
            RawSerializerOptions.AddTransportHeaders | RawSerializerOptions.CopyHeaders);
        rabbit.Send<IntegrationEnvelopeV1<ExtractVideoPayloadV1>>(topology =>
            topology.UseSerializer("application/json"));

        rabbit.ReceiveEndpoint(QueueNames.Orchestrator, endpoint =>
        {
            endpoint.SetQuorumQueue();
            endpoint.PrefetchCount = 16;
            endpoint.UseMessageRetry(retry => retry.Intervals(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15)));
            endpoint.UseEntityFrameworkOutbox<OrchestratorDbContext>(context);
            endpoint.ConfigureConsumer<VideoAnalysisRequestedConsumer>(context);
            endpoint.ConfigureConsumer<TranscriptAnalyzedConsumer>(context);
            endpoint.ConfigureConsumer<TranscriptAnalysisFailedConsumer>(context);
        });

        rabbit.ReceiveEndpoint(QueueNames.ExtractorEvents, endpoint =>
        {
            endpoint.SetQuorumQueue();
            endpoint.PrefetchCount = 8;
            endpoint.ConfigureConsumeTopology = false;
            endpoint.UseRawJsonDeserializer(RawSerializerOptions.All, isDefault: true);
            endpoint.UseMessageRetry(retry => retry.Intervals(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15)));
            endpoint.UseEntityFrameworkOutbox<OrchestratorDbContext>(context);
            endpoint.ConfigureConsumer<ExtractorEventConsumer>(context);
        });
    });
});

var host = builder.Build();
if (host.Services.GetRequiredService<IConfiguration>().GetValue("Database:ApplyMigrations", true))
{
    await using var scope = host.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
    await db.Database.MigrateAsync();
}
await host.RunAsync();
