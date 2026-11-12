using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using YouTubeProcessing.Api.Data;
using YouTubeProcessing.Contracts;

namespace YouTubeProcessing.Api.Health;

public sealed class PlatformHealthService(
    ApiDbContext db,
    IAmazonS3 s3,
    IRabbitMqManagementProbe rabbitMq,
    IOptions<PlatformOptions> platformOptions,
    IOptions<PlatformHealthOptions> healthOptions,
    ILogger<PlatformHealthService> logger)
{
    public async Task<PlatformHealthResponse> CheckAsync(CancellationToken cancellationToken)
    {
        var postgresTask = CheckPostgresAsync(cancellationToken);
        var blobTask = CheckBlobStoreAsync(cancellationToken);
        var rabbitTask = CheckRabbitMqAsync(cancellationToken);
        await Task.WhenAll(postgresTask, blobTask, rabbitTask);

        var postgres = await postgresTask;
        var blobStore = await blobTask;
        var rabbitResult = await rabbitTask;
        var services = new Dictionary<string, ComponentHealth>(StringComparer.Ordinal)
        {
            ["api"] = MicroserviceHealthEvaluator.Evaluate(
                rabbitResult.Snapshot,
                rabbitResult.Health.DurationMilliseconds,
                QueueNames.ApiProjection),
            ["postgres"] = postgres,
            ["rabbitMq"] = rabbitResult.Health,
            ["blobStore"] = blobStore,
            ["orchestrator"] = MicroserviceHealthEvaluator.Evaluate(
                rabbitResult.Snapshot,
                rabbitResult.Health.DurationMilliseconds,
                QueueNames.Orchestrator,
                QueueNames.ExtractorEvents),
            ["extractor"] = MicroserviceHealthEvaluator.Evaluate(
                rabbitResult.Snapshot,
                rabbitResult.Health.DurationMilliseconds,
                QueueNames.ExtractorCommands),
            ["analyzer"] = healthOptions.Value.AnalyzerEnabled
                ? MicroserviceHealthEvaluator.Evaluate(
                    rabbitResult.Snapshot,
                    rabbitResult.Health.DurationMilliseconds,
                    QueueNames.Analyzer)
                : new ComponentHealth(
                    HealthStatuses.Disabled,
                    0,
                    "Transcript analysis is disabled by configuration.")
        };

        var overallStatus = services.Values.Any(service =>
            service.Status == HealthStatuses.Unhealthy)
                ? HealthStatuses.Unhealthy
                : HealthStatuses.Healthy;
        return new PlatformHealthResponse(
            overallStatus,
            DateTimeOffset.UtcNow,
            services);
    }

    private async Task<ComponentHealth> CheckPostgresAsync(
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            "Postgres",
            async token => await db.Database.CanConnectAsync(token)
                ? new ComponentHealth(
                    HealthStatuses.Healthy,
                    0,
                    "The API database accepted a connection.")
                : new ComponentHealth(
                    HealthStatuses.Unhealthy,
                    0,
                    "The API database did not accept a connection."),
            cancellationToken);

    private async Task<ComponentHealth> CheckBlobStoreAsync(
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            "Blob store",
            async token =>
            {
                await s3.ListObjectsV2Async(
                    new ListObjectsV2Request
                    {
                        BucketName = platformOptions.Value.Blob.Bucket,
                        MaxKeys = 1
                    },
                    token);
                return new ComponentHealth(
                    HealthStatuses.Healthy,
                    0,
                    "The configured artifact bucket is reachable.");
            },
            cancellationToken);

    private async Task<RabbitMqCheckResult> CheckRabbitMqAsync(
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(healthOptions.Value.TimeoutSeconds));
            using var combined = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
            var snapshot = await rabbitMq.InspectAsync(combined.Token);
            return new RabbitMqCheckResult(
                new ComponentHealth(
                    HealthStatuses.Healthy,
                    stopwatch.ElapsedMilliseconds,
                    "RabbitMQ management and queue state are reachable."),
                snapshot);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("RabbitMQ health check timed out");
            return RabbitMqCheckResult.Unhealthy(
                stopwatch.ElapsedMilliseconds,
                "RabbitMQ did not respond before the health-check timeout.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "RabbitMQ health check failed");
            return RabbitMqCheckResult.Unhealthy(
                stopwatch.ElapsedMilliseconds,
                "RabbitMQ management is unavailable.");
        }
    }

    private async Task<ComponentHealth> ExecuteAsync(
        string component,
        Func<CancellationToken, Task<ComponentHealth>> check,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(healthOptions.Value.TimeoutSeconds));
            using var combined = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
            var result = await check(combined.Token);
            return result with { DurationMilliseconds = stopwatch.ElapsedMilliseconds };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("{Component} health check timed out", component);
            return new ComponentHealth(
                HealthStatuses.Unhealthy,
                stopwatch.ElapsedMilliseconds,
                $"{component} did not respond before the health-check timeout.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "{Component} health check failed", component);
            return new ComponentHealth(
                HealthStatuses.Unhealthy,
                stopwatch.ElapsedMilliseconds,
                $"{component} is unavailable.");
        }
    }

    private static QueueHealth ToQueueHealth(RabbitMqQueueSnapshot queue) =>
        new(
            IsQueueHealthy(queue) ? HealthStatuses.Healthy : HealthStatuses.Unhealthy,
            queue.Consumers);

    private static bool IsQueueHealthy(RabbitMqQueueSnapshot queue) =>
        queue.Consumers > 0 &&
        string.Equals(queue.State, "running", StringComparison.OrdinalIgnoreCase);

    private sealed record RabbitMqCheckResult(
        ComponentHealth Health,
        RabbitMqSnapshot? Snapshot)
    {
        public static RabbitMqCheckResult Unhealthy(long duration, string description) =>
            new(
                new ComponentHealth(
                    HealthStatuses.Unhealthy,
                    duration,
                    description),
                null);
    }

    public static class MicroserviceHealthEvaluator
    {
        public static ComponentHealth Evaluate(
            RabbitMqSnapshot? snapshot,
            long durationMilliseconds,
            params string[] requiredQueues)
        {
            if (snapshot is null)
            {
                return new ComponentHealth(
                    HealthStatuses.Unhealthy,
                    durationMilliseconds,
                    "Worker status is unknown because RabbitMQ is unavailable.");
            }

            var queues = requiredQueues.ToDictionary(
                queueName => queueName,
                queueName => snapshot.Queues.TryGetValue(queueName, out var queue)
                    ? ToQueueHealth(queue)
                    : new QueueHealth(HealthStatuses.Unhealthy, 0),
                StringComparer.Ordinal);
            var isHealthy = queues.Values.All(queue =>
                queue.Status == HealthStatuses.Healthy);
            return new ComponentHealth(
                isHealthy ? HealthStatuses.Healthy : HealthStatuses.Unhealthy,
                durationMilliseconds,
                isHealthy
                    ? "All required message consumers are connected."
                    : "One or more required message consumers are unavailable.",
                queues);
        }
    }
}
