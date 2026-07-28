using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YouTubeProcessing.Contracts;
using YouTubeProcessing.Orchestrator.Data;

namespace YouTubeProcessing.Orchestrator;

public sealed class WorkflowOptions
{
    public const string SectionName = "Workflow";
    public bool AnalysisEnabled { get; init; }
    public int ExtractionTimeoutMinutes { get; init; } = 15;
    public int AnalysisTimeoutMinutes { get; init; } = 10;
    public int ScanIntervalSeconds { get; init; } = 30;
}

public sealed class WorkflowTimeoutService(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkflowOptions> options,
    ILogger<WorkflowTimeoutService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(Math.Max(5, options.Value.ScanIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await FailExpiredProcesses(stoppingToken);
        }
    }

    private async Task FailExpiredProcesses(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var now = DateTimeOffset.UtcNow;
        var extractionCutoff = now.AddMinutes(-options.Value.ExtractionTimeoutMinutes);
        var analysisCutoff = now.AddMinutes(-options.Value.AnalysisTimeoutMinutes);

        var expired = await db.Processes
            .Where(x =>
                (x.Stage == JobStages.Extracting && x.UpdatedAt < extractionCutoff) ||
                (x.Stage == JobStages.Analyzing && x.UpdatedAt < analysisCutoff))
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var process in expired)
        {
            var failedStage = process.Stage;
            process.Stage = JobStages.Failed;
            process.UpdatedAt = now;
            await publisher.Publish(
                new VideoAnalysisFailedV1(
                    process.JobId,
                    process.AnalysisId,
                    failedStage,
                    "WORKFLOW_TIMEOUT",
                    $"The {failedStage} stage exceeded its configured timeout.",
                    now),
                context => context.CorrelationId = process.JobId,
                cancellationToken);
            logger.LogWarning(
                "Timed out job {JobId} in stage {Stage}",
                process.JobId,
                failedStage);
        }

        if (expired.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
