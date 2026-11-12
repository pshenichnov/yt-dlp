using Amazon.S3;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YouTubeProcessing.Analyzer;
using YouTubeProcessing.Analyzer.Analysis;
using YouTubeProcessing.Analyzer.Consumers;
using YouTubeProcessing.Analyzer.Data;
using YouTubeProcessing.Contracts;
using YouTubeProcessing.Observability;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services.AddOptions<AnalyzerOptions>()
    .Bind(builder.Configuration.GetSection(AnalyzerOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        settings => settings.Provider.Equals("codex", StringComparison.OrdinalIgnoreCase) ||
                    settings.Provider.Equals("deterministic", StringComparison.OrdinalIgnoreCase),
        "Analyzer:Provider must be 'codex' or 'deterministic'.")
    .Validate(
        settings => settings.ReasoningEffort is "minimal" or "low" or "medium" or
                    "high" or "xhigh",
        "Analyzer:ReasoningEffort must be one of: minimal, low, medium, high, xhigh.")
    .ValidateOnStart();
builder.Services.AddDbContext<AnalyzerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("AnalyzerDatabase")));
builder.Services.AddPlatformObservability(builder.Configuration, "youtube-processing-analyzer");

builder.Services.AddSingleton<IAmazonS3>(services =>
{
    var settings = services.GetRequiredService<IOptions<AnalyzerOptions>>().Value.Blob;
    return new AmazonS3Client(
        settings.AccessKey,
        settings.SecretKey,
        new AmazonS3Config
        {
            ServiceURL = settings.Endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = settings.Region
        });
});

builder.Services.AddSingleton<ICodexProcessRunner, CodexProcessRunner>();
builder.Services.AddSingleton<CodexTranscriptAnalyzer>();
builder.Services.AddSingleton<DeterministicTranscriptAnalyzer>();
builder.Services.AddScoped<ITranscriptAnalyzer>(services =>
    services.GetRequiredService<IOptions<AnalyzerOptions>>().Value.Provider
        .Equals("deterministic", StringComparison.OrdinalIgnoreCase)
            ? services.GetRequiredService<DeterministicTranscriptAnalyzer>()
            : services.GetRequiredService<CodexTranscriptAnalyzer>());

builder.Services.AddMassTransit(configurator =>
{
    configurator.SetKebabCaseEndpointNameFormatter();
    configurator.AddConsumer<AnalyzeTranscriptConsumer>();
    configurator.AddEntityFrameworkOutbox<AnalyzerDbContext>(outbox =>
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
        rabbit.ReceiveEndpoint(QueueNames.Analyzer, endpoint =>
        {
            endpoint.SetQuorumQueue();
            endpoint.PrefetchCount = 4;
            endpoint.UseMessageRetry(retry => retry.Intervals(
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30)));
            endpoint.UseEntityFrameworkOutbox<AnalyzerDbContext>(context);
            endpoint.ConfigureConsumer<AnalyzeTranscriptConsumer>(context);
        });
    });
});

var host = builder.Build();
if (host.Services.GetRequiredService<IConfiguration>().GetValue("Database:ApplyMigrations", true))
{
    await using var scope = host.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AnalyzerDbContext>();
    await db.Database.MigrateAsync();
}
await host.RunAsync();
