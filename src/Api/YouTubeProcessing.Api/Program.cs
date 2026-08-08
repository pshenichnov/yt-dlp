using Amazon.S3;
using MassTransit;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using YouTubeProcessing.Api;
using YouTubeProcessing.Api.Consumers;
using YouTubeProcessing.Api.Data;
using YouTubeProcessing.Api.Endpoints;
using YouTubeProcessing.Api.Infrastructure;
using YouTubeProcessing.Contracts;
using YouTubeProcessing.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services
    .AddOptions<PlatformOptions>()
    .Bind(builder.Configuration.GetSection(PlatformOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<ApiKeyOpenApiDocumentTransformer>());
builder.Services.AddPlatformObservability(builder.Configuration, "youtube-processing-api");
builder.Services.AddDbContext<ApiDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("ApiDatabase")));

builder.Services.AddSingleton<IAmazonS3>(services =>
{
    var settings = services.GetRequiredService<IOptions<PlatformOptions>>().Value.Blob;
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

builder.Services.AddMassTransit(configurator =>
{
    configurator.SetKebabCaseEndpointNameFormatter();
    configurator.AddConsumer<JobStageChangedConsumer>();
    configurator.AddConsumer<VideoAnalysisCompletedConsumer>();
    configurator.AddConsumer<VideoAnalysisFailedConsumer>();
    configurator.AddEntityFrameworkOutbox<ApiDbContext>(outbox =>
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

        rabbit.ReceiveEndpoint(QueueNames.ApiProjection, endpoint =>
        {
            endpoint.SetQuorumQueue();
            endpoint.PrefetchCount = 16;
            endpoint.UseMessageRetry(retry => retry.Intervals(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15)));
            endpoint.UseEntityFrameworkOutbox<ApiDbContext>(context);
            endpoint.ConfigureConsumer<JobStageChangedConsumer>(context);
            endpoint.ConfigureConsumer<VideoAnalysisCompletedConsumer>(context);
            endpoint.ConfigureConsumer<VideoAnalysisFailedConsumer>(context);
        });
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";
    options.SwaggerEndpoint("/openapi/v1.json", "YouTube Processing API v1");
    options.DocumentTitle = "YouTube Processing API";
    options.DisplayRequestDuration();
});

app.MapOpenApi();
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }))
    .AllowAnonymous();
app.MapGet("/health/ready", async (ApiDbContext db, CancellationToken cancellationToken) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "ready" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();
app.MapVideoAnalysisEndpoints();

if (app.Configuration.GetValue("Database:ApplyMigrations", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    await db.Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
